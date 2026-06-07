using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfCustomPropertiesService"/>. Тонкий orchestrator: валидация
/// аргументов + I/O + offload в <see cref="Task.Run(Action, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfCustomPropertiesCosReader"/> / <see cref="PdfCustomPropertiesCosWriter"/>. Список
/// best-effort (битый / без <c>/Info</c> PDF → пустой список), запись атомарна (temp + Move) и не
/// мутирует source.
/// </summary>
public sealed class PdfPigCustomPropertiesService : IPdfCustomPropertiesService
{
    private readonly ILogger<PdfPigCustomPropertiesService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики чтения.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigCustomPropertiesService(ILogger<PdfPigCustomPropertiesService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PdfCustomProperty>> ListAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ListSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Custom-property listing is best-effort: any failure (corrupt PDF, missing /Info) yields empty list, not a throw.")]
    private IReadOnlyList<PdfCustomProperty> ListSafe(byte[] bytes, string path)
    {
        try
        {
            return PdfCustomPropertiesCosReader.Read(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read PDF custom properties from '{Path}'; returning empty.", path);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string sourcePath,
        string targetPath,
        IReadOnlyList<PdfCustomProperty> customProperties,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(customProperties);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task
            .Run(() => PdfCustomPropertiesCosWriter.Write(source, customProperties), ct)
            .ConfigureAwait(false);

        _log.LogDebug(
            "Writing {Count} custom document propert(ies) into PDF '{Target}'.",
            customProperties.Count, targetPath);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static async Task WriteAtomicAsync(string targetPath, byte[] bytes, CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup; temp residue не критичен.
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup.
        }
    }
}
