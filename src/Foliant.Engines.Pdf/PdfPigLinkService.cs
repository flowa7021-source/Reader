using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfLinkService"/>. Тонкий orchestrator: валидация аргументов + I/O +
/// offload в <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>; вся cos-логика — в
/// <see cref="PdfLinkCosReader"/>. Чтение best-effort: битый PDF / документ без ссылок → пустой список
/// (без throw'а). Только чтение, записи нет.
/// </summary>
public sealed class PdfPigLinkService : IPdfLinkService
{
    private readonly ILogger<PdfPigLinkService> _log;

    /// <summary>Создаёт сервис с логгером для best-effort диагностики чтения.</summary>
    /// <param name="log">Логгер.</param>
    public PdfPigLinkService(ILogger<PdfPigLinkService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PdfLinkAnnotation>> ListLinksAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        byte[] bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);
        return await Task.Run(() => ReadSafe(bytes, pdfPath), ct).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Link listing is best-effort: any failure (corrupt PDF, missing /Annots) yields an empty list, not a throw.")]
    private IReadOnlyList<PdfLinkAnnotation> ReadSafe(byte[] bytes, string path)
    {
        try
        {
            return PdfLinkCosReader.Read(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read PDF links from '{Path}'; returning empty.", path);
            return [];
        }
    }
}
