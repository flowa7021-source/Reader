using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfXmpService"/>. Тонкий orchestrator: валидация аргументов + I/O +
/// offload в <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfXmpCosReader"/> / <see cref="PdfXmpCosWriter"/>. Чтение best-effort (битый / без
/// <c>/Metadata</c> PDF → <see langword="null"/>), запись атомарна (temp + Move) и не мутирует source.
/// </summary>
public sealed class PdfPigXmpService : IPdfXmpService
{
    private readonly ILogger<PdfPigXmpService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики чтения.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigXmpService(ILogger<PdfPigXmpService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ReadSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "XMP read is best-effort: any failure (corrupt PDF, missing /Metadata, unsupported filter) yields null, not a throw.")]
    private string? ReadSafe(byte[] bytes, string path)
    {
        try
        {
            return PdfXmpCosReader.Read(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read PDF XMP metadata from '{Path}'; returning null.", path);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string sourcePath, string targetPath, string xmpPacket, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(xmpPacket);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => PdfXmpCosWriter.Write(source, xmpPacket), ct).ConfigureAwait(false);

        _log.LogDebug("Writing XMP metadata ({Size} bytes) into PDF '{Target}'.", output.Length, targetPath);
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
