using System.Runtime.InteropServices;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using PDFiumCore;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Header/footer integration tests via real PDFium runtime.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumHeaderFooterServiceTests : IDisposable
{
    private const int TextObjectType = 1;

    private readonly string _tmpDir;
    private readonly PdfiumHeaderFooterService _service = new();

    public PdfiumHeaderFooterServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-headerfooter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task ApplyAsync_HeaderAndFooter_AddsTwoTextObjectsPerPage()
    {
        string src = WritePdf(pageCount: 3);
        string dst = Path.Combine(_tmpDir, "stamped.pdf");
        var spec = HeaderFooterSpec.FromCenterTexts(
            headerText: "Confidential",
            footerText: "Page {page} of {total}",
            fontSize: 10,
            r: 0, g: 0, b: 0);

        await _service.ApplyAsync(src, spec, dst, default);

        WithDocument(dst, doc =>
        {
            int pageCount = fpdfview.FPDF_GetPageCount(doc);
            pageCount.Should().Be(3);
            for (int i = 0; i < pageCount; i++)
            {
                var page = fpdfview.FPDF_LoadPage(doc, i);
                try
                {
                    int textObjs = CountTextObjects(page);
                    // Source PDF: 1 text per page + 2 added (header + footer) = 3.
                    textObjs.Should().BeGreaterThanOrEqualTo(3);
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
            }
        });
    }

    [Fact]
    public async Task ApplyAsync_HeaderOnly_AddsOneObjectPerPage()
    {
        string src = WritePdf(pageCount: 2);
        string dst = Path.Combine(_tmpDir, "stamped.pdf");
        var spec = HeaderFooterSpec.FromCenterTexts(headerText: "Top", footerText: null, fontSize: 10, r: 0, g: 0, b: 0);

        await _service.ApplyAsync(src, spec, dst, default);

        WithDocument(dst, doc =>
        {
            for (int i = 0; i < 2; i++)
            {
                var page = fpdfview.FPDF_LoadPage(doc, i);
                try
                {
                    // Header-only: ≥1 source text + 1 header. Footer не прибавляет.
                    // Точное число может отличаться по PdfPig-внутренним факторам, но header'а
                    // достаточно для покрытия.
                    CountTextObjects(page).Should().BeGreaterThanOrEqualTo(2);
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
            }
        });
    }

    [Fact]
    public async Task ApplyAsync_BothEmpty_Throws()
    {
        string src = WritePdf(pageCount: 1);
        var spec = HeaderFooterSpec.FromCenterTexts(headerText: null, footerText: "   ", fontSize: 10, r: 0, g: 0, b: 0);

        var act = () => _service.ApplyAsync(src, spec, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyAsync_ZeroFontSize_Throws()
    {
        string src = WritePdf(pageCount: 1);
        var spec = HeaderFooterSpec.FromCenterTexts(headerText: "x", footerText: null, fontSize: 0, r: 0, g: 0, b: 0);

        var act = () => _service.ApplyAsync(src, spec, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private string WritePdf(int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pageCount; i++)
        {
            var page = builder.AddPage(width: 200, height: 200);
            page.AddText($"Body {i + 1}", 12, new UglyToad.PdfPig.Core.PdfPoint(20, 100), font);
        }

        string path = Path.Combine(_tmpDir, "src-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static int CountTextObjects(FpdfPageT page)
    {
        int total = fpdf_edit.FPDFPageCountObjects(page);
        int textObjs = 0;
        for (int i = 0; i < total; i++)
        {
            var obj = fpdf_edit.FPDFPageGetObject(page, i);
            if (obj is not null && fpdf_edit.FPDFPageObjGetType(obj) == TextObjectType)
            {
                textObjs++;
            }
        }

        return textObjs;
    }

    private static void WithDocument(string path, Action<FpdfDocumentT> body)
    {
        fpdfview.FPDF_InitLibrary();
        byte[] bytes = File.ReadAllBytes(path);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            doc.Should().NotBeNull();
            try
            {
                body(doc);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(doc);
            }
        }
        finally
        {
            pin.Free();
        }
    }
}
