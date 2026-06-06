using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfSanitizationService"/>. Тонкий orchestrator: валидация аргументов +
/// I/O + offload в <see cref="Task.Run{TResult}(System.Func{TResult}, CancellationToken)"/>; вся
/// cos-логика — в <see cref="PdfSanitizationCosReader"/> / <see cref="PdfSanitizationCosWriter"/>.
/// Сканирование best-effort (битый / без скриптов PDF → пустой отчёт), удаление атомарно (temp + Move)
/// и не мутирует source.
/// </summary>
public sealed class PdfPigSanitizationService : IPdfSanitizationService
{
    private readonly ILogger<PdfPigSanitizationService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики сканирования.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigSanitizationService(ILogger<PdfPigSanitizationService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<PdfSanitizationReport> ScanAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ScanSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Sanitization scan is best-effort: any failure (corrupt PDF, missing catalog scripts) yields an empty report, not a throw.")]
    private PdfSanitizationReport ScanSafe(byte[] bytes, string path)
    {
        try
        {
            return PdfSanitizationCosReader.Read(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to scan PDF for document JavaScript/actions from '{Path}'; returning empty report.", path);
            return new PdfSanitizationReport([], HasJavaScriptOpenAction: false, HasDocumentAdditionalActions: false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveJavaScriptAndActionsAsync(
        string sourcePath, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        (byte[] output, bool removedAnything) = await Task
            .Run(() => RemoveCore(source), ct)
            .ConfigureAwait(false);

        _log.LogDebug(
            "Sanitizing document JavaScript/actions into PDF '{Target}' (removedAnything={Removed}).",
            targetPath, removedAnything);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
        return removedAnything;
    }

    private static (byte[] Output, bool RemovedAnything) RemoveCore(byte[] source)
    {
        byte[] output = PdfSanitizationCosWriter.Remove(source, out bool removedAnything);
        return (output, removedAnything);
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
