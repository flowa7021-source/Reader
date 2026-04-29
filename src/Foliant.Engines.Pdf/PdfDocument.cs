using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

internal sealed class PdfDocument : IDocument
{
    private readonly FpdfDocumentT _doc;
    private readonly Lock _gate = new();
    private bool _disposed;

    public DocumentKind Kind => DocumentKind.Pdf;
    public int PageCount { get; }
    public DocumentMetadata Metadata { get; }

    public PdfDocument(FpdfDocumentT doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _doc = doc;

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

    public IDocumentEditor? GetEditor() => null;

    public IFormController? GetForms() => null;

    public ISignatureController? GetSignatures() => null;

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
                // Native FPDF_CloseDocument failure during DisposeAsync — nothing actionable,
                // but trace so the failure is not invisible during debugging.
                Debug.WriteLine($"PdfDocument.DisposeAsync: FPDF_CloseDocument threw: {ex}");
            }
        }

        return ValueTask.CompletedTask;
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

                // PDFiumCore 146.x dropped the underscore between `FPDFBitmap` and the verb
                // (FPDFBitmap_CreateEx → FPDFBitmapCreateEx); FPDF_DWORD args are now `ulong`.
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

                    if (opts.Theme == RenderTheme.Dark || opts.Theme == RenderTheme.HighContrast)
                    {
                        // TODO (S6): HighContrast — implement proper high-contrast palette.
                        // Phase 1: invert B, G, R; leave alpha intact.
                        InvertBgr(bytes);
                    }

                    return new PdfPageRender(wPx, hPx, stride, bytes);
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

    private TextLayer? GetTextLayerCore(int pageIndex)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            var page = fpdfview.FPDF_LoadPage(_doc, pageIndex);
            try
            {
                float wPt = fpdfview.FPDF_GetPageWidthF(page);
                float hPt = fpdfview.FPDF_GetPageHeightF(page);

                // PDFiumCore 146.x: `FPDFText_LoadPage` → `FPDFTextLoadPage`, etc.
                var tp = fpdf_text.FPDFTextLoadPage(page);
                try
                {
                    int count = fpdf_text.FPDFTextCountChars(tp);
                    if (count <= 0)
                    {
                        return TextLayer.Empty(pageIndex);
                    }

                    // PDFium uses UTF-16LE; the new binding takes a `ref ushort` and pins it
                    // for the duration of the call, so we use a managed ushort[] and
                    // re-interpret it as Span<char> for the string ctor (zero-copy).
                    ushort[] buffer = new ushort[count + 1];
                    fpdf_text.FPDFTextGetText(tp, 0, count, ref buffer[0]);
                    ReadOnlySpan<char> chars = MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, count));
                    string text = new(chars);

                    // Phase 1 simplification: one TextRun covering the whole page.
                    // Detailed word positions deferred to S6.
                    var run = new TextRun(text, 0, 0, wPt, hPt);
                    return new TextLayer(pageIndex, [run]);
                }
                finally
                {
                    fpdf_text.FPDFTextClosePage(tp);
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
        // FPDF_GetMetaText writes UTF-16LE to a void* buffer; length is in bytes including null.
        // PDFiumCore 146.x widened FPDF_DWORD to ulong (uint64).
        const int BufBytes = 1024;
        IntPtr buf = Marshal.AllocHGlobal(BufBytes);
        try
        {
            ulong len = fpdf_doc.FPDF_GetMetaText(doc, tag, buf, (ulong)BufBytes);
            if (len <= 2)
            {
                return null;
            }

            string value = Marshal.PtrToStringUni(buf) ?? string.Empty;
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

        // Strip leading "D:" prefix if present.
        ReadOnlySpan<char> s = raw.AsSpan();
        if (s.StartsWith("D:", StringComparison.Ordinal))
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

        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
    }

    private static int ComputePixels(float points, double zoom, int? maxPx)
    {
        // 72 PDF points = 1 inch; standard screen = 96 DPI → multiply by 96/72.
        double px = points * zoom * 96.0 / 72.0;
        if (maxPx.HasValue && px > maxPx.Value)
        {
            px = maxPx.Value;
        }

        return Math.Max(1, (int)Math.Round(px));
    }

    private static void InvertBgr(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = (byte)(255 - bytes[i]);         // B
            bytes[i + 1] = (byte)(255 - bytes[i + 1]); // G
            bytes[i + 2] = (byte)(255 - bytes[i + 2]); // R
            // bytes[i + 3] = alpha — leave unchanged
        }
    }
}
