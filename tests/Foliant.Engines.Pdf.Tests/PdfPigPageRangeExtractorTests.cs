using FluentAssertions;
using Foliant.Engines.Pdf;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

public sealed class PdfPigPageRangeExtractorTests : IDisposable
{
    private readonly string _tmpDir;

    public PdfPigPageRangeExtractorTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-extract-" + Guid.NewGuid().ToString("N"));
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
    public async Task ExtractAsync_RangeInsideDocument_WritesOnlyRequestedPages()
    {
        string src = WritePdf(pageCount: 5);
        string dst = Path.Combine(_tmpDir, "chapter.pdf");

        await new PdfPigPageRangeExtractor().ExtractAsync(src, firstPageIndex: 1, lastPageIndexInclusive: 3, dst, default);

        File.Exists(dst).Should().BeTrue();
        using var doc = PdfPigDocument.Open(dst);
        doc.NumberOfPages.Should().Be(3);
        // Page-tagged content: исходные страницы помечены 1-based ярлыком, проверяем что взяты именно 2,3,4.
        var texts = doc.GetPages().Select(p => p.Text.Trim()).ToList();
        texts.Should().Equal("Page 2", "Page 3", "Page 4");
    }

    [Fact]
    public async Task ExtractAsync_SinglePage_WritesOnePageDocument()
    {
        string src = WritePdf(pageCount: 3);
        string dst = Path.Combine(_tmpDir, "one.pdf");

        await new PdfPigPageRangeExtractor().ExtractAsync(src, firstPageIndex: 0, lastPageIndexInclusive: 0, dst, default);

        using var doc = PdfPigDocument.Open(dst);
        doc.NumberOfPages.Should().Be(1);
        doc.GetPage(1).Text.Trim().Should().Be("Page 1");
    }

    [Fact]
    public async Task ExtractAsync_LastPageOutOfRange_Throws()
    {
        string src = WritePdf(pageCount: 2);
        var sut = new PdfPigPageRangeExtractor();

        var act = () => sut.ExtractAsync(src, 0, 5, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExtractAsync_FirstGreaterThanLast_Throws()
    {
        string src = WritePdf(pageCount: 5);
        var sut = new PdfPigPageRangeExtractor();

        var act = () => sut.ExtractAsync(src, 3, 1, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExtractAsync_NegativeFirst_Throws()
    {
        string src = WritePdf(pageCount: 5);
        var sut = new PdfPigPageRangeExtractor();

        var act = () => sut.ExtractAsync(src, -1, 1, Path.Combine(_tmpDir, "out.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExtractAsync_WritesAtomic_TmpDoesNotLinger()
    {
        string src = WritePdf(pageCount: 3);
        string dst = Path.Combine(_tmpDir, "out.pdf");

        await new PdfPigPageRangeExtractor().ExtractAsync(src, 0, 1, dst, default);

        // Tmp файл создавался в той же папке, должен быть подчищен.
        Directory.EnumerateFiles(_tmpDir, ".*.tmp").Should().BeEmpty();
        Directory.EnumerateFiles(_tmpDir, "*.tmp").Should().BeEmpty();
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
}
