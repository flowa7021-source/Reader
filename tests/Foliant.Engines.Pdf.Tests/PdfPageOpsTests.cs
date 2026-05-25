using FluentAssertions;
using UglyToad.PdfPig;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Integration roundtrip tests for <see cref="PdfPageOps"/>. These exercise PdfPig's
/// managed writer (no PDFium native needed) — building a multipage PDF, applying a
/// structure op, then reopening to assert page count/order. Page order is verified by
/// each page's unique MediaBox height stamped by <see cref="MultiPagePdfFactory"/>.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfPageOpsTests : IDisposable
{
    private readonly string _tmpDir;

    public PdfPageOpsTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-pageops-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task DeletePage_RemovesPage_ShiftsOrder()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(4));

        byte[] result = await PdfPageOps.DeletePageAsync(src, index: 1, default);

        HeightsOf(result).Should().Equal(
            MultiPagePdfFactory.HeightOfPage(0),
            MultiPagePdfFactory.HeightOfPage(2),
            MultiPagePdfFactory.HeightOfPage(3));
    }

    [Fact]
    public async Task ReorderPages_AppliesPermutation()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(3));

        byte[] result = await PdfPageOps.ReorderPagesAsync(src, [2, 0, 1], default);

        HeightsOf(result).Should().Equal(
            MultiPagePdfFactory.HeightOfPage(2),
            MultiPagePdfFactory.HeightOfPage(0),
            MultiPagePdfFactory.HeightOfPage(1));
    }

    [Fact]
    public async Task InsertPages_InsertsOtherDocAtIndex()
    {
        string baseDoc = WritePdf(MultiPagePdfFactory.Create(2, baseHeightPt: 800));
        string other = WritePdf(MultiPagePdfFactory.Create(2, baseHeightPt: 500));

        byte[] result = await PdfPageOps.InsertPagesAsync(baseDoc, other, atIndex: 1, default);

        // [base0] [other0] [other1] [base1]
        HeightsOf(result).Should().Equal(
            MultiPagePdfFactory.HeightOfPage(0, 800),
            MultiPagePdfFactory.HeightOfPage(0, 500),
            MultiPagePdfFactory.HeightOfPage(1, 500),
            MultiPagePdfFactory.HeightOfPage(1, 800));
    }

    [Fact]
    public async Task InsertPages_AtEnd_Appends()
    {
        string baseDoc = WritePdf(MultiPagePdfFactory.Create(2, baseHeightPt: 800));
        string other = WritePdf(MultiPagePdfFactory.Create(1, baseHeightPt: 500));

        byte[] result = await PdfPageOps.InsertPagesAsync(baseDoc, other, atIndex: 2, default);

        PageCountOf(result).Should().Be(3);
        HeightsOf(result).Last().Should().BeApproximately(MultiPagePdfFactory.HeightOfPage(0, 500), 1.0);
    }

    [Fact]
    public async Task RotatePage_SetsRotateOnTargetPageOnly()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(3));

        byte[] result = await PdfPageOps.RotatePageAsync(src, index: 1, Domain.ViewRotation.Cw90, default);

        using var doc = PdfDocument.Open(result);
        doc.GetPage(1).Rotation.Value.Should().Be(0);
        doc.GetPage(2).Rotation.Value.Should().Be(90);
        doc.GetPage(3).Rotation.Value.Should().Be(0);
    }

    [Fact]
    public async Task RotatePage_ComposesWithExistingRotation_Wraps()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(1));
        byte[] once = await PdfPageOps.RotatePageAsync(src, 0, Domain.ViewRotation.Cw270, default);

        string mid = WritePdf(once);
        byte[] twice = await PdfPageOps.RotatePageAsync(mid, 0, Domain.ViewRotation.Cw180, default);

        // 270 + 180 = 450 ≡ 90 (mod 360)
        using var doc = PdfDocument.Open(twice);
        doc.GetPage(1).Rotation.Value.Should().Be(90);
    }

    [Fact]
    public async Task WriteToPath_ProducesValidReopenablePdf()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(3));
        string outPath = Path.Combine(_tmpDir, "out.pdf");

        await PdfPageOps.DeletePageAsync(src, outPath, index: 0, default);

        File.Exists(outPath).Should().BeTrue();
        using var doc = PdfDocument.Open(outPath);
        doc.NumberOfPages.Should().Be(2);
        Directory.GetFiles(_tmpDir, "*.tmp").Should().BeEmpty("temp file must be moved, not left behind");
    }

    [Fact]
    public async Task DeletePage_BadIndex_ThrowsArgumentOutOfRange()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(2));

        var act = async () => await PdfPageOps.DeletePageAsync(src, index: 5, default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RotatePage_BadIndex_ThrowsArgumentOutOfRange()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(2));

        var act = async () => await PdfPageOps.RotatePageAsync(src, index: 9, Domain.ViewRotation.Cw90, default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ReorderPages_NonPermutation_Throws()
    {
        string src = WritePdf(MultiPagePdfFactory.Create(3));

        var act = async () => await PdfPageOps.ReorderPagesAsync(src, [0, 0, 1], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private string WritePdf(byte[] bytes)
    {
        string path = Path.Combine(_tmpDir, $"doc-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static int PageCountOf(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return doc.NumberOfPages;
    }

    private static double[] HeightsOf(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return doc.GetPages().Select(p => p.Height).ToArray();
    }
}
