using System.Runtime.InteropServices;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

// Text-layer extraction вынесена в partial-файл, чтобы PdfDocument.cs оставался
// ≤300 строк. Логика не менялась — только перенесена.
internal sealed partial class PdfDocument
{
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
        if (written <= 0)
        {
            return null;
        }
        // The probe (buflen=0) reported `count` chars excluding the terminator. Whether the
        // second call's return value includes the trailing NUL is PDFium-build-dependent, so
        // clamp to `count` instead of `written - 1` (which dropped the last char on builds
        // that exclude the terminator).
        int chars = Math.Min(written, count);
        string text = new(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, chars)));

        // Canonical TextRun (PageGeometry/Annotation): X=left, Y=bottom, Y up.
        return string.IsNullOrWhiteSpace(text)
            ? null
            : new TextRun(text, X: left, Y: bottom, W: right - left, H: top - bottom);
    }
}
