using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Записывает /Outlines в PDF через cos-инкрементальный апдейт (симметрично
/// <see cref="PdfPigOutlineReader"/>'у). Читает <c>sourcePath</c> целиком в память, строит outline
/// дерево (<see cref="PdfOutlineCosWriter"/>), дописывает его новыми объектами + обновлённым
/// catalog'ом, пишет результат атомарно в <c>targetPath</c> (temp+Move). Оригинал не мутируется —
/// инкрементальный апдейт не трогает исходные байты (ISO 32000-1 §7.5.6), а <c>targetPath</c>
/// читается из памяти, так что source == target безопасно.
///
/// Тонкий orchestrator: валидация аргументов + I/O + offload в Task.Run; вся cos-логика — в
/// helper'ах (<see cref="PdfOutlineCosWriter"/>, <see cref="OutlineLinks"/>, <see cref="PdfTextString"/>,
/// <see cref="PdfCatalogOutlineCosWriter"/>).
/// </summary>
public sealed class PdfPigOutlineWriter : IPdfOutlineWriter
{
    private readonly ILogger<PdfPigOutlineWriter> _log;

    public PdfPigOutlineWriter(ILogger<PdfPigOutlineWriter> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task WriteOutlineAsync(
        string sourcePath, string targetPath, IReadOnlyList<DocumentOutlineEntry> entries, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(entries);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => PdfOutlineCosWriter.WriteOutline(source, entries), ct).ConfigureAwait(false);

        _log.LogDebug("Writing {Count} outline entries into PDF '{Target}'.", entries.Count, targetPath);
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
