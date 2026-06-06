using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfNamedDestinationService"/>. Тонкий orchestrator: валидация
/// аргументов + I/O + offload в <see cref="Task.Run(Action, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfNamedDestinationCosReader"/> / <see cref="PdfNamedDestinationCosWriter"/>. Список
/// best-effort (битый / без пунктов назначения PDF → пустой список) и учитывает обе формы хранения;
/// запись атомарна (temp + Move), идёт в modern-форму и не мутирует source.
/// </summary>
public sealed class PdfPigNamedDestinationService : IPdfNamedDestinationService
{
    private readonly ILogger<PdfPigNamedDestinationService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики чтения.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigNamedDestinationService(ILogger<PdfPigNamedDestinationService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PdfNamedDestination>> ListAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ListSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Named-destination listing is best-effort: any failure (corrupt PDF, missing /Dests, malformed destination) yields empty list, not a throw.")]
    private IReadOnlyList<PdfNamedDestination> ListSafe(byte[] bytes, string path)
    {
        try
        {
            return PdfNamedDestinationCosReader.Read(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list PDF named destinations from '{Path}'; returning empty.", path);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(
        string sourcePath, string targetPath, string name, int pageIndex, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task
            .Run(() => PdfNamedDestinationCosWriter.Add(source, name, pageIndex), ct)
            .ConfigureAwait(false);

        _log.LogDebug("Adding named destination '{Name}' → page index {PageIndex} into PDF '{Target}'.",
            name, pageIndex, targetPath);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string sourcePath, string targetPath, string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task
            .Run(() => PdfNamedDestinationCosWriter.Remove(source, name), ct)
            .ConfigureAwait(false);

        _log.LogDebug("Removing named destination '{Name}' from PDF '{Target}'.", name, targetPath);
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
