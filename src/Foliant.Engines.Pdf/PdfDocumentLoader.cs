using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Распознаёт PDF по расширению И/ИЛИ заголовку <c>%PDF-</c>.
/// LoadAsync открывает документ через PDFiumCore и пробрасывает зависимости,
/// нужные редактору (event store + fingerprint), чтобы <see cref="IDocument.GetEditor"/>
/// мог лениво построить <c>PdfDocumentEditor</c>. Зависимости опциональны: если DI их
/// не предоставил (или это unit-тест), документ открывается read-only (GetEditor → null).
/// </summary>
public sealed class PdfDocumentLoader(
    ILogger<PdfDocumentLoader> log,
    IEventStore? eventStore = null,
    IFileFingerprint? fingerprint = null) : IDocumentLoader
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

    public async Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Fingerprint считаем здесь (async), чтобы синхронный GetEditor() не делал
        // sync-over-async мост к ComputeAsync (контракт качества §0).
        string? fingerprintHex = fingerprint is null
            ? null
            : await fingerprint.ComputeAsync(path, ct).ConfigureAwait(false);

        return await Task.Run<IDocument>(() =>
        {
            PdfLibrary.EnsureInitialized();
            var doc = fpdfview.FPDF_LoadDocument(path, null);
            if (doc is null)
            {
                var err = fpdfview.FPDF_GetLastError();
                throw new InvalidOperationException($"PDFium failed to load '{path}': error {err}");
            }

            log.LogDebug("Loaded PDF '{Path}' via PDFium", path);
            return new PdfDocument(doc, path, fingerprintHex, eventStore);
        }, ct).ConfigureAwait(false);
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
