using Foliant.Domain;
using Microsoft.Extensions.Logging;
using PDFiumCore;

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
        return Task.Run<IDocument>(() =>
        {
            PdfLibrary.EnsureInitialized();
            var doc = fpdfview.FPDF_LoadDocument(path, null);
            if (doc is null)
            {
                var err = fpdfview.FPDF_GetLastError();
                throw new InvalidOperationException($"PDFium failed to load '{path}': error {err}");
            }

            log.LogDebug("Loaded PDF '{Path}' via PDFium", path);
            return new PdfDocument(doc);
        }, ct);
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
