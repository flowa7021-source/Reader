using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfOcgEditService"/> (rename / delete OCG-слоёв). Тонкий
/// orchestrator: валидация аргументов + I/O + offload в
/// <see cref="Task.Run(Action, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfOcgEditCosWriter"/>. Запись атомарна (temp + Move) и не мутирует source
/// (паттерн <see cref="PdfPigPageLabelService"/> / watermark / redact); запись поверх того же
/// пути (<c>sourcePath == targetPath</c>) безопасна — байты читаются в память до записи.
///
/// <para>Managed-only (PdfPig + raw cos-write): класс не держит native-ресурсов и не
/// использует общий PDFium-lock. <c>layerIndex</c> валидируется против числа слоёв из
/// снимка <see cref="PdfOcgCosReader"/> до записи (out-of-range →
/// <see cref="ArgumentOutOfRangeException"/>).</para>
/// </summary>
public sealed class PdfPigOcgEditService : IPdfOcgEditService
{
    private readonly ILogger<PdfPigOcgEditService> _log;

    /// <summary>Создаёт сервис с логгером для диагностики записи.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigOcgEditService(ILogger<PdfPigOcgEditService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task RenameAsync(
        string sourcePath, string targetPath, int layerIndex, string newName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                ValidateLayerIndex(source, layerIndex);
                return PdfOcgEditCosWriter.Rename(source, layerIndex, newName);
            },
            ct).ConfigureAwait(false);

        _log.LogDebug("Renaming OCG layer #{Index} in PDF '{Target}'.", layerIndex, targetPath);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string sourcePath, string targetPath, int layerIndex, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                ValidateLayerIndex(source, layerIndex);
                return PdfOcgEditCosWriter.Remove(source, layerIndex);
            },
            ct).ConfigureAwait(false);

        _log.LogDebug("Removing OCG layer #{Index} from PDF '{Target}'.", layerIndex, targetPath);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static void ValidateLayerIndex(byte[] source, int layerIndex)
    {
        var snapshot = PdfOcgCosReader.Read(source);
        ArgumentOutOfRangeException.ThrowIfNegative(layerIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layerIndex, snapshot.Layers.Count);
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
