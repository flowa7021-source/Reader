using System.Globalization;
using FluentAssertions;
using Foliant.Engines.Pdf;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Exercises <see cref="PdfPigSplitService"/> against the committed 10-page text asset.
/// Pure-managed (PdfPig) — green under the Linux unit filter, no PDFium needed.
/// Each asset page begins with "Page {N} of 10", so page identity/order is assertable.
/// </summary>
public sealed class PdfPigSplitServiceTests : IDisposable
{
    private readonly string _tmpDir;

    public PdfPigSplitServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-split-" + Guid.NewGuid().ToString("N"));
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
    public async Task SplitEveryAsync_ChunkSmallerThanDoc_ProducesNumberedFilesWithRemainder()
    {
        var paths = await new PdfPigSplitService().SplitEveryAsync(Asset, pagesPerChunk: 3, _tmpDir, "part", default);

        paths.Should().HaveCount(4);
        paths.Select(Path.GetFileName).Should().Equal("part-001.pdf", "part-002.pdf", "part-003.pdf", "part-004.pdf");
        paths.Select(PageCount).Should().Equal(3, 3, 3, 1);
        // Каждый кусок открывается PdfPig и непрерывен от исходной позиции.
        FirstPageLabel(paths[0]).Should().StartWith("Page 1 of 10");
        FirstPageLabel(paths[3]).Should().StartWith("Page 10 of 10");
    }

    [Fact]
    public async Task SplitEveryAsync_ChunkAtLeastPageCount_ProducesSingleFileWithAllPages()
    {
        var paths = await new PdfPigSplitService().SplitEveryAsync(Asset, pagesPerChunk: 25, _tmpDir, "whole", default);

        paths.Should().ContainSingle();
        Path.GetFileName(paths[0]).Should().Be("whole-001.pdf");
        PageCount(paths[0]).Should().Be(10);
    }

    [Fact]
    public async Task SplitEveryAsync_NonZeroPaddingFollowsInvariantCulture()
    {
        // chunk=1 → 10 файлов; имена 001..010 не зависят от текущей культуры.
        using (new CultureScope(CultureInfo.GetCultureInfo("ar-SA")))
        {
            var paths = await new PdfPigSplitService().SplitEveryAsync(Asset, pagesPerChunk: 1, _tmpDir, "p", default);

            paths.Should().HaveCount(10);
            Path.GetFileName(paths[0]).Should().Be("p-001.pdf");
            Path.GetFileName(paths[9]).Should().Be("p-010.pdf");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task SplitEveryAsync_NonPositiveChunk_Throws(int chunk)
    {
        var act = () => new PdfPigSplitService().SplitEveryAsync(Asset, chunk, _tmpDir, "x", default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExtractSelectionAsync_NonContiguousSelection_WritesPagesInOrder()
    {
        string dst = Path.Combine(_tmpDir, "selection.pdf");

        await new PdfPigSplitService().ExtractSelectionAsync(Asset, [0, 2, 6, 9], dst, default);

        PageLabels(dst).Should().Equal("Page 1 of 10", "Page 3 of 10", "Page 7 of 10", "Page 10 of 10");
    }

    [Fact]
    public async Task ExtractSelectionAsync_ReorderedSelection_PreservesRequestedOrder()
    {
        string dst = Path.Combine(_tmpDir, "reorder.pdf");

        await new PdfPigSplitService().ExtractSelectionAsync(Asset, [9, 0], dst, default);

        PageCount(dst).Should().Be(2);
        PageLabels(dst).Should().Equal("Page 10 of 10", "Page 1 of 10");
    }

    [Fact]
    public async Task ExtractSelectionAsync_IndexOutOfRange_Throws()
    {
        var act = () => new PdfPigSplitService().ExtractSelectionAsync(Asset, [0, 10], Path.Combine(_tmpDir, "oob.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExtractSelectionAsync_NegativeIndex_Throws()
    {
        var act = () => new PdfPigSplitService().ExtractSelectionAsync(Asset, [-1, 2], Path.Combine(_tmpDir, "neg.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExtractSelectionAsync_EmptySelection_Throws()
    {
        var act = () => new PdfPigSplitService().ExtractSelectionAsync(Asset, [], Path.Combine(_tmpDir, "empty.pdf"), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WritesAtomic_NoTmpFilesLinger()
    {
        await new PdfPigSplitService().SplitEveryAsync(Asset, pagesPerChunk: 4, _tmpDir, "atomic", default);

        Directory.EnumerateFiles(_tmpDir, ".*.tmp").Should().BeEmpty();
        Directory.EnumerateFiles(_tmpDir, "*.tmp").Should().BeEmpty();
    }

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static string FirstPageLabel(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.GetPage(1).Text;
    }

    private static IReadOnlyList<string> PageLabels(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        // Каждая страница начинается с "Page {N} of 10" — вырезаем ровно этот ярлык (до текста) для проверки порядка.
        return doc.GetPages().Select(p => PageLabel(p.Text)).ToList();
    }

    private static string PageLabel(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^Page \d+ of 10");
        match.Success.Should().BeTrue("asset pages are tagged 'Page N of 10'");
        return match.Value;
    }

    private static string ResolveAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(CultureInfo culture)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = culture;
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
