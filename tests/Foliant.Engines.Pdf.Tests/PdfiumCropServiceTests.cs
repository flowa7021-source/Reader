using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using PDFiumCore;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Slow round-trip: создаём минимальный PDF через PdfPig, гоняем через <see cref="PdfiumCropService"/>
/// в обоих режимах, читаем обратно MediaBox через PDFium и проверяем, что физический режим
/// фактически уменьшает страницу, а обратимый — оставляет MediaBox исходным.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumCropServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfiumCropService _crop;

    public PdfiumCropServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _crop = new PdfiumCropService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string MakeSamplePdf(double widthPt = 612, double heightPt = 792)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(width: widthPt, height: heightPt);
        page.AddText("Sample", 12, new UglyToad.PdfPig.Core.PdfPoint(100, 100), font);
        string path = Path.Combine(_tmpDir, "in-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static (float L, float B, float R, float T) ReadMediaBox(string pdfPath)
    {
        // PDFium already initialised by ApplyAsync calls earlier in each test.
        byte[] bytes = File.ReadAllBytes(pdfPath);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            try
            {
                var page = fpdfview.FPDF_LoadPage(doc, 0);
                try
                {
                    float l = 0, b = 0, r = 0, t = 0;
                    fpdf_transformpage.FPDFPageGetMediaBox(page, ref l, ref b, ref r, ref t);
                    return (l, b, r, t);
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
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

    [Fact]
    public async Task ApplyAsync_ReversibleMode_LeavesMediaBoxUnchanged()
    {
        string source = MakeSamplePdf(widthPt: 600, heightPt: 800);
        string target = Path.Combine(_tmpDir, "rev.pdf");

        await _crop.ApplyAsync(
            source,
            new CropSpec(Left: 0.1, Top: 0.1, Right: 0.1, Bottom: 0.1, CropMode.Reversible),
            target,
            CancellationToken.None);

        var (l, b, r, t) = ReadMediaBox(target);
        // MediaBox stays 0..600 by 0..800 — only CropBox changed.
        (r - l).Should().BeApproximately(600f, 1f);
        (t - b).Should().BeApproximately(800f, 1f);
    }

    [Fact]
    public async Task ApplyAsync_PhysicalMode_ShrinksMediaBox()
    {
        string source = MakeSamplePdf(widthPt: 600, heightPt: 800);
        string target = Path.Combine(_tmpDir, "phys.pdf");

        await _crop.ApplyAsync(
            source,
            new CropSpec(Left: 0.1, Top: 0.1, Right: 0.1, Bottom: 0.1, CropMode.Physical),
            target,
            CancellationToken.None);

        var (l, b, r, t) = ReadMediaBox(target);
        // Trim 10 % per side → width: 600 * 0.8 = 480, height: 800 * 0.8 = 640.
        (r - l).Should().BeApproximately(480f, 1f);
        (t - b).Should().BeApproximately(640f, 1f);
        // Origin shifts to (60, 80) because we trimmed from left/bottom too.
        l.Should().BeApproximately(60f, 1f);
        b.Should().BeApproximately(80f, 1f);
    }

    [Fact]
    public async Task ApplyAsync_NoEffectSpec_Throws()
    {
        string source = MakeSamplePdf();
        Func<Task> act = async () => await _crop.ApplyAsync(
            source,
            new CropSpec(0, 0, 0, 0),
            Path.Combine(_tmpDir, "out.pdf"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyAsync_InvalidSpec_Throws()
    {
        string source = MakeSamplePdf();
        Func<Task> act = async () => await _crop.ApplyAsync(
            source,
            new CropSpec(0.6, 0, 0, 0),  // > 0.5
            Path.Combine(_tmpDir, "out.pdf"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
