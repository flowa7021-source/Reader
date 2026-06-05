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
    IFileFingerprint? fingerprint = null) : IDocumentLoader, IPasswordAwareDocumentLoader
{
    // PDFiumCore не экспонирует именованную константу для кодов FPDF_GetLastError.
    // 4 = FPDF_ERR_PASSWORD из публичного PDFium API (fpdf_view.h): «требуется/неверный
    // пароль». Сравниваем с ulong-результатом FPDF_GetLastError() (uint расширяется до ulong).
    private const uint FpdfErrPassword = 4;

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

    public Task<IDocument> LoadAsync(string path, CancellationToken ct) =>
        LoadCoreAsync(path, password: null, ct);

    // Реализация IPasswordAwareDocumentLoader: открытие зашифрованного PDF с паролем.
    public Task<IDocument> LoadAsync(string path, string? password, CancellationToken ct) =>
        LoadCoreAsync(path, password, ct);

    private async Task<IDocument> LoadCoreAsync(string path, string? password, CancellationToken ct)
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

            // PDFium сам расшифровывает (AES/RC4) по переданному паролю; null/неверный
            // пароль на зашифрованном файле → FPDF_LoadDocument возвращает null + код
            // FPDF_ERR_PASSWORD, который мы транслируем в типизированное исключение,
            // чтобы VM могла спросить пароль и повторить.
            var doc = fpdfview.FPDF_LoadDocument(path, password);
            if (doc is null)
            {
                ulong err = fpdfview.FPDF_GetLastError();
                if (err == FpdfErrPassword)
                {
                    throw DocumentPasswordRequiredException.ForPath(path);
                }

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
