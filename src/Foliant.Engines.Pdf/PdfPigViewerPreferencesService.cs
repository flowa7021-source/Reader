using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfViewerPreferencesService"/>. Тонкий orchestrator: валидация
/// аргументов + I/O + offload в <see cref="Task.Run(Action, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfViewerPreferencesCosReader"/> / <see cref="PdfViewerPreferencesCosWriter"/>. Чтение
/// best-effort (битый / без-настроек PDF → <see cref="PdfViewerPreferences.Default"/>), запись
/// атомарна (temp + Move) и не мутирует source.
/// </summary>
public sealed class PdfPigViewerPreferencesService : IPdfViewerPreferencesService
{
    private readonly ILogger<PdfPigViewerPreferencesService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики чтения.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigViewerPreferencesService(ILogger<PdfPigViewerPreferencesService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<PdfViewerPreferences> ReadAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ReadSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Viewer-preferences read is best-effort: any failure (corrupt PDF, missing keys) yields Default, not a throw.")]
    private PdfViewerPreferences ReadSafe(byte[] bytes, string path)
    {
        try
        {
            return PdfViewerPreferencesCosReader.Read(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read PDF viewer preferences from '{Path}'; returning default.", path);
            return PdfViewerPreferences.Default;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        string sourcePath, string targetPath, PdfViewerPreferences prefs, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(prefs);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => PdfViewerPreferencesCosWriter.Write(source, prefs), ct).ConfigureAwait(false);

        _log.LogDebug("Writing viewer preferences into PDF '{Target}'.", targetPath);
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
