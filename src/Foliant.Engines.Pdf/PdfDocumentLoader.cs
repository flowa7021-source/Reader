using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Распознаёт PDF по расширению И/ИЛИ заголовку <c>%PDF-</c>.
/// LoadAsync — заглушка для S1. Реальная реализация через PDFiumCore — там же.
/// </summary>
public sealed class PdfDocumentLoader(ILogger<PdfDocumentLoader> log) : IDocumentLoader
{
    private static readonly byte[] Magic = "%PDF-"u8.ToArray();

    public DocumentKind Kind => DocumentKind.Pdf;

    public bool CanLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (".pdf".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasPdfMagic(path);
    }

    public Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        log.LogDebug("PdfDocumentLoader.LoadAsync для {Path} — реализуется в S1", path);
        throw new NotImplementedException(
            "Реальная загрузка через PDFiumCore — спринт S1 (см. IMPLEMENTATION_PLAN.md разделы 4 и 5.2).");
    }

    private static bool HasPdfMagic(string path)
    {
        Span<byte> head = stackalloc byte[Magic.Length];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var read = fs.Read(head);
            return read == Magic.Length && head.SequenceEqual(Magic);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
