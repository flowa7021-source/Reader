using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Engines.Pdf.Editing;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

internal sealed partial class PdfDocument : IDocument
{
    private readonly FpdfDocumentT _doc;
    private readonly Lock _gate = new();
    private readonly string? _path;
    private readonly string? _fingerprintHex;
    private readonly IEventStore? _eventStore;
    private PdfDocumentEditor? _editor;
    private bool _disposed;

    public DocumentKind Kind => DocumentKind.Pdf;
    public int PageCount { get; }
    public DocumentMetadata Metadata { get; }

    public PdfDocument(
        FpdfDocumentT doc,
        string? path = null,
        string? fingerprintHex = null,
        IEventStore? eventStore = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _doc = doc;
        _path = path;
        _fingerprintHex = fingerprintHex;
        _eventStore = eventStore;

        lock (_gate)
        {
            PageCount = fpdfview.FPDF_GetPageCount(_doc);
            Metadata = ReadMetadata(_doc);
        }
    }

    public PageSize GetPageSize(int pageIndex)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var page = fpdfview.FPDF_LoadPage(_doc, pageIndex);
            try
            {
                float w = fpdfview.FPDF_GetPageWidthF(page);
                float h = fpdfview.FPDF_GetPageHeightF(page);
                return new PageSize(w, h);
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    public Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return Task.Run<IPageRender>(() => RenderPageCore(pageIndex, opts), ct);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct) =>
        Task.Run<TextLayer?>(() => GetTextLayerCore(pageIndex), ct);

    public IDocumentEditor? GetEditor()
    {
        // Null-safe: старые call-sites/тесты могут не передать path/store/fingerprint —
        // тогда документ read-only и редактор недоступен.
        if (_path is null || _fingerprintHex is null || _eventStore is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _editor ??= BuildEditor(_path, _fingerprintHex, _eventStore);
        }
    }

    public IFormController? GetForms() => null;

    /// <summary>Read-only signatures controller (Q-F25). <c>null</c> для in-memory-документов
    /// без path'а — <see cref="PdfSignatureController"/> открывает свою PDFium-сессию по path'у.</summary>
    public ISignatureController? GetSignatures() =>
        _path is null ? null : new PdfSignatureController(_path);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "DisposeAsync must not throw; close failure is logged via Debug trace and swallowed.")]
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;

            try
            {
                fpdfview.FPDF_CloseDocument(_doc);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PdfDocument.DisposeAsync: FPDF_CloseDocument threw: {ex}");
            }
        }

        return ValueTask.CompletedTask;
    }

    private static PdfDocumentEditor BuildEditor(string path, string fingerprintHex, IEventStore eventStore)
    {
        // FRAGILE: IO — базовый снимок = байты файла на момент открытия редактора.
        // Fingerprint предвычислен в loader'е (async), здесь sync-over-async моста нет.
        byte[] baseBytes = File.ReadAllBytes(path);
        return new PdfDocumentEditor(baseBytes, fingerprintHex, eventStore, path);
    }

    private PdfPageRender RenderPageCore(int pageIndex, RenderOptions opts)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var page = fpdfview.FPDF_LoadPage(_doc, pageIndex);
            try
            {
                float wPt = fpdfview.FPDF_GetPageWidthF(page);
                float hPt = fpdfview.FPDF_GetPageHeightF(page);

                int wPx = ComputePixels(wPt, opts.Zoom, opts.MaxWidthPx);
                int hPx = ComputePixels(hPt, opts.Zoom, opts.MaxHeightPx);

                // FRAGILE: PDFiumCore 146.x dropped the underscore (FPDFBitmap_CreateEx → FPDFBitmapCreateEx); FPDF_DWORD is now `ulong`.
                var bmp = fpdfview.FPDFBitmapCreateEx(wPx, hPx, 4, IntPtr.Zero, 0);
                try
                {
                    fpdfview.FPDFBitmapFillRect(bmp, 0, 0, wPx, hPx, 0xFFFFFFFFUL);

                    int flags = opts.RenderAnnotations ? 1 : 0; // FPDF_ANNOT = 1
                    fpdfview.FPDF_RenderPageBitmap(bmp, page, 0, 0, wPx, hPx, 0, flags);

                    IntPtr ptr = fpdfview.FPDFBitmapGetBuffer(bmp);
                    int stride = fpdfview.FPDFBitmapGetStride(bmp);

                    byte[] bytes = new byte[stride * hPx];
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);

                    RenderColorMap.ApplyTheme(bytes, opts.Theme);

                    return new PdfPageRender(wPx, hPx, stride, bytes, new PageSize(wPt, hPt));
                }
                finally
                {
                    fpdfview.FPDFBitmapDestroy(bmp);
                }
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    private static DocumentMetadata ReadMetadata(FpdfDocumentT doc)
    {
        return new DocumentMetadata(
            Title: GetMeta(doc, "Title"),
            Author: GetMeta(doc, "Author"),
            Subject: GetMeta(doc, "Subject"),
            Created: ParsePdfDate(GetMeta(doc, "CreationDate")),
            Modified: ParsePdfDate(GetMeta(doc, "ModDate")),
            Custom: new Dictionary<string, string>());
    }

    private static string? GetMeta(FpdfDocumentT doc, string tag)
    {
        // FRAGILE: FPDF_GetMetaText writes UTF-16LE; len is bytes incl. NUL; FPDF_DWORD is ulong.
        const int BufBytes = 1024;
        IntPtr buf = Marshal.AllocHGlobal(BufBytes);
        try
        {
            ulong len = fpdf_doc.FPDF_GetMetaText(doc, tag, buf, (ulong)BufBytes);
            if (len <= 2)
            {
                return null;
            }

            // `len` is the full byte length incl. NUL and may exceed BufBytes when the value
            // was truncated; bound the read to the buffer so PtrToStringUni can't scan past it.
            int bytesInBuf = (int)Math.Min(len, (ulong)BufBytes);
            int charCount = (bytesInBuf / 2) - 1; // UTF-16 units, excluding trailing NUL
            string value = charCount <= 0 ? string.Empty : Marshal.PtrToStringUni(buf, charCount);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    internal static DateTimeOffset? ParsePdfDate(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        ReadOnlySpan<char> s = raw.AsSpan();
        if (s.StartsWith("D:", StringComparison.Ordinal)) // strip optional "D:" prefix
        {
            s = s[2..];
        }

        if (s.Length < 8)
        {
            return null;
        }

        if (!int.TryParse(s[..4], out int year) ||
            !int.TryParse(s[4..6], out int month) ||
            !int.TryParse(s[6..8], out int day))
        {
            return null;
        }

        // A malformed PDF date (e.g. month 13) must not throw while reading metadata on open.
        try
        {
            return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int ComputePixels(float points, double zoom, int? maxPx)
    {
        double px = points * zoom * 96.0 / 72.0; // 72 pt = 1 inch; screen 96 DPI
        if (maxPx.HasValue && px > maxPx.Value)
        {
            px = maxPx.Value;
        }

        return Math.Max(1, (int)Math.Round(px));
    }
}
