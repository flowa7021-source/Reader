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

                    if (opts.Theme == RenderTheme.Dark || opts.Theme == RenderTheme.HighContrast)
                    {
                        // TODO (S6): HighContrast palette; Phase 1 inverts B,G,R, alpha intact.
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

            // FRAGILE: native interop — FPDF_Load/ClosePage balanced in finally.
            var page = fpdfview.FPDF_LoadPage(_doc, pageIndex);
            try
            {
                return BuildTextLayer(pageIndex, page);
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    private static TextLayer BuildTextLayer(int pageIndex, FpdfPageT page)
    {
        var tp = fpdf_text.FPDFTextLoadPage(page); // FRAGILE: native interop — paired with FPDFTextClosePage in finally
        try
        {
            // FRAGILE: native interop — CountChars ≤0 ⇒ image-only page; CountRects(0,-1) merges chars into per-line boxes.
            if (fpdf_text.FPDFTextCountChars(tp) <= 0)
            {
                return TextLayer.Empty(pageIndex);
            }

            int rectCount = fpdf_text.FPDFTextCountRects(tp, 0, -1);
            var runs = new List<TextRun>(Math.Max(0, rectCount));
            for (int i = 0; i < rectCount; i++)
            {
                if (ReadRectRun(tp, i) is { } run)
                {
                    runs.Add(run);
                }
            }
            return runs.Count == 0 ? TextLayer.Empty(pageIndex) : new TextLayer(pageIndex, runs);
        }
        finally
        {
            fpdf_text.FPDFTextClosePage(tp);
        }
    }

    private static TextRun? ReadRectRun(FpdfTextpageT tp, int rectIndex)
    {
        // FRAGILE: native interop — GetRect uses `ref double` (NOT out), returns int bool; coords PDF page space (pt, Y up, top > bottom).
        double left = 0, top = 0, right = 0, bottom = 0;
        if (fpdf_text.FPDFTextGetRect(tp, rectIndex, ref left, ref top, ref right, ref bottom) == 0)
        {
            return null;
        }

        // FRAGILE: native interop — GetBoundedText(buflen=0) returns UTF-16 count excl. NUL; buffer is `ref ushort` UTF-16LE.
        ushort probe = 0;
        int count = fpdf_text.FPDFTextGetBoundedText(tp, left, top, right, bottom, ref probe, 0);
        if (count <= 0)
        {
            return null;
        }

        ushort[] buffer = new ushort[count + 1]; // +1 for terminating NUL
        int written = fpdf_text.FPDFTextGetBoundedText(tp, left, top, right, bottom, ref buffer[0], buffer.Length);
        int chars = Math.Clamp(written - 1, 0, count); // `written` includes NUL when space allowed
        string text = new(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, chars)));

        // Canonical TextRun (PageGeometry/Annotation): X=left, Y=bottom, Y up.
        return string.IsNullOrWhiteSpace(text)
            ? null
            : new TextRun(text, X: left, Y: bottom, W: right - left, H: top - bottom);
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

        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
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

    private static void InvertBgr(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = (byte)(255 - bytes[i]);         // B
            bytes[i + 1] = (byte)(255 - bytes[i + 1]); // G
            bytes[i + 2] = (byte)(255 - bytes[i + 2]); // R; bytes[i + 3] = alpha, unchanged
        }
    }
}
