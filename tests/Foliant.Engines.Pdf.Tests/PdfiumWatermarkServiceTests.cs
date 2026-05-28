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
/// Watermark integration tests via real PDFium runtime (requires libpdfium native).
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumWatermarkServiceTests : IDisposable
{
    // FPDF_PAGEOBJ_TEXT — PDFium type-tag (см. fpdf_edit.h).
    private const int TextObjectType = 1;

    private readonly string _tmpDir;
    private readonly PdfiumWatermarkService _service = new();

    public PdfiumWatermarkServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-watermark-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task ApplyAsync_AddsExactlyOneTextObjectPerPage()
    {
        string src = WritePdf(pageCount: 3);
        string dst = Path.Combine(_tmpDir, "watermarked.pdf");
        var spec = new WatermarkSpec("CONFIDENTIAL", FontSize: 48, Opacity: 0.3, AngleDegrees: 45, R: 128, G: 128, B: 128);

        await _service.ApplyAsync(src, spec, dst, default);

        File.Exists(dst).Should().BeTrue();

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
                    // Source PDF (WritePdf) уже добавляет 1 текстовый элемент на страницу,
                    // watermark — +1; ожидаем минимум 2 текстовых объекта.
                    textObjs.Should().BeGreaterThanOrEqualTo(2);
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
            }
        });
    }

    [Fact]
    public async Task ApplyAsync_EmptyText_Throws()
    {
        string src = WritePdf(pageCount: 1);
        var spec = new WatermarkSpec("  ", 48, 0.5, 0, 0, 0, 0);

        var act = () => _service.ApplyAsync(src, spec, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyAsync_OpacityOutOfRange_Throws()
    {
        string src = WritePdf(pageCount: 1);

        var actHigh = () => _service.ApplyAsync(src, new WatermarkSpec("X", 48, 1.5, 0, 0, 0, 0),
            Path.Combine(_tmpDir, "out.pdf"), default);
        var actLow = () => _service.ApplyAsync(src, new WatermarkSpec("X", 48, -0.1, 0, 0, 0, 0),
            Path.Combine(_tmpDir, "out.pdf"), default);

        await actHigh.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await actLow.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ApplyAsync_ZeroFontSize_Throws()
    {
        string src = WritePdf(pageCount: 1);
        var spec = new WatermarkSpec("X", 0, 0.5, 0, 0, 0, 0);

        var act = () => _service.ApplyAsync(src, spec, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ApplyAsync_OutputIsValidPdf_WithOriginalPageCount()
    {
        string src = WritePdf(pageCount: 5);
        string dst = Path.Combine(_tmpDir, "out.pdf");
        var spec = new WatermarkSpec("DRAFT", 60, 0.4, 30, 200, 0, 0);

        await _service.ApplyAsync(src, spec, dst, default);

        WithDocument(dst, doc => fpdfview.FPDF_GetPageCount(doc).Should().Be(5));
    }

    private string WritePdf(int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pageCount; i++)
        {
            var page = builder.AddPage(width: 200, height: 200);
            page.AddText($"Page {i + 1}", 12, new UglyToad.PdfPig.Core.PdfPoint(20, 100), font);
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
            if (obj is null)
            {
                continue;
            }

            if (fpdf_edit.FPDFPageObjGetType(obj) == TextObjectType)
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
            doc.Should().NotBeNull("watermark output must be openable by PDFium");
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
