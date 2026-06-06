using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfAttachmentService"/>. Тонкий orchestrator: валидация аргументов +
/// I/O + offload в <see cref="Task.Run(Action, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfAttachmentCosReader"/> / <see cref="PdfAttachmentCosWriter"/>. Список best-effort
/// (битый / без вложений PDF → пустой список), запись атомарна (temp + Move) и не мутирует source.
/// </summary>
public sealed class PdfPigAttachmentService : IPdfAttachmentService
{
    private readonly ILogger<PdfPigAttachmentService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики чтения.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigAttachmentService(ILogger<PdfPigAttachmentService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PdfAttachment>> ListAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ListSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Attachment listing is best-effort: any failure (corrupt PDF, missing /EmbeddedFiles, unsupported filter) yields empty list, not a throw.")]
    private List<PdfAttachment> ListSafe(byte[] bytes, string path)
    {
        try
        {
            var entries = PdfAttachmentCosReader.Read(bytes);
            var result = new List<PdfAttachment>(entries.Count);
            foreach (var e in entries)
            {
                result.Add(new PdfAttachment(e.Name, e.Size, e.Description));
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list PDF attachments from '{Path}'; returning empty.", path);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task ExtractAsync(
        string pdfPath, string attachmentName, string targetFilePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        byte[] payload = await Task.Run(() => ExtractBytes(bytes, attachmentName), ct).ConfigureAwait(false);

        _log.LogDebug("Extracting attachment '{Name}' to '{Target}'.", attachmentName, targetFilePath);
        await WriteAtomicAsync(targetFilePath, payload, ct).ConfigureAwait(false);
    }

    private static byte[] ExtractBytes(byte[] bytes, string attachmentName)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var entries = PdfAttachmentCosReader.Read(doc);
        foreach (var e in entries)
        {
            if (string.Equals(e.Name, attachmentName, StringComparison.Ordinal))
            {
                return PdfAttachmentCosReader.ExtractBytes(doc, e.StreamRef);
            }
        }

        throw new KeyNotFoundException($"No PDF attachment named '{attachmentName}'.");
    }

    /// <inheritdoc />
    public async Task AddAsync(
        string sourcePath, string targetPath, string fileToAttachPath, string? description, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileToAttachPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] fileBytes = await File.ReadAllBytesAsync(fileToAttachPath, ct).ConfigureAwait(false);
        string fileName = Path.GetFileName(fileToAttachPath);

        byte[] output = await Task
            .Run(() => PdfAttachmentCosWriter.Add(source, fileName, fileBytes, description), ct)
            .ConfigureAwait(false);

        _log.LogDebug("Embedding attachment '{Name}' ({Size} bytes) into PDF '{Target}'.",
            fileName, fileBytes.Length, targetPath);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        string sourcePath, string targetPath, string attachmentName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentName);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task
            .Run(() => PdfAttachmentCosWriter.Remove(source, attachmentName), ct)
            .ConfigureAwait(false);

        _log.LogDebug("Removing attachment '{Name}' from PDF '{Target}'.", attachmentName, targetPath);
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
