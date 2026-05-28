using FluentAssertions;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

public sealed class PdfPigOutlineReaderTests : IDisposable
{
    private readonly string _tmpDir;

    public PdfPigOutlineReaderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-outline-reader-" + Guid.NewGuid().ToString("N"));
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
    public async Task ReadAsync_NoOutline_ReturnsEmpty()
    {
        string path = WritePdf(pageCount: 3, bookmarks: null);
        var reader = new PdfPigOutlineReader(NullLogger<PdfPigOutlineReader>.Instance);

        var entries = await reader.ReadAsync(path, default);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_FlatOutline_ReturnsEntriesInOrderWithPageZeroBased()
    {
        var bookmarks = new Bookmarks(
        [
            DocBookmark("Chapter 1", page: 1),
            DocBookmark("Chapter 2", page: 2),
            DocBookmark("Chapter 3", page: 3),
        ]);
        string path = WritePdf(pageCount: 3, bookmarks);
        var reader = new PdfPigOutlineReader(NullLogger<PdfPigOutlineReader>.Instance);

        var entries = await reader.ReadAsync(path, default);

        entries.Should().HaveCount(3);
        entries[0].Title.Should().Be("Chapter 1");
        entries[0].PageIndex.Should().Be(0);
        entries[0].Depth.Should().Be(0);
        entries[1].Title.Should().Be("Chapter 2");
        entries[1].PageIndex.Should().Be(1);
        entries[2].Title.Should().Be("Chapter 3");
        entries[2].PageIndex.Should().Be(2);
    }

    [Fact]
    public async Task ReadAsync_NestedOutline_FlattensWithIncreasingDepth()
    {
        // Chapter 1 -> Section 1.1 -> Sub 1.1.1
        // Chapter 2
        var sub = DocBookmark("Sub 1.1.1", page: 1);
        var section = DocBookmark("Section 1.1", page: 1, children: [sub]);
        var chapter1 = DocBookmark("Chapter 1", page: 1, children: [section]);
        var chapter2 = DocBookmark("Chapter 2", page: 2);

        string path = WritePdf(pageCount: 2, new Bookmarks([chapter1, chapter2]));
        var reader = new PdfPigOutlineReader(NullLogger<PdfPigOutlineReader>.Instance);

        var entries = await reader.ReadAsync(path, default);

        entries.Select(e => (e.Title, e.Depth)).Should().Equal(
            ("Chapter 1", 0),
            ("Section 1.1", 1),
            ("Sub 1.1.1", 2),
            ("Chapter 2", 0));
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsEmpty_NotThrow()
    {
        var reader = new PdfPigOutlineReader(NullLogger<PdfPigOutlineReader>.Instance);

        var entries = await reader.ReadAsync(Path.Combine(_tmpDir, "does-not-exist.pdf"), default);

        // Контракт: best-effort; читатель не должен ронять вкладку из-за битого файла.
        entries.Should().BeEmpty();
    }

    private string WritePdf(int pageCount, Bookmarks? bookmarks)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pageCount; i++)
        {
            var page = builder.AddPage(width: 200, height: 200);
            page.AddText($"Page {i + 1}", 12, new UglyToad.PdfPig.Core.PdfPoint(20, 100), font);
        }

        if (bookmarks is not null)
        {
            builder.Bookmarks = bookmarks;
        }

        string path = Path.Combine(_tmpDir, "doc-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static DocumentBookmarkNode DocBookmark(string title, int page, IReadOnlyList<BookmarkNode>? children = null)
    {
        // Level в PdfPig'овском конструкторе — informational; читатель пересчитывает глубину сам.
        var dest = new ExplicitDestination(
            page,
            ExplicitDestinationType.FitPage,
            ExplicitDestinationCoordinates.Empty);
        return new DocumentBookmarkNode(title, level: 0, dest, children ?? []);
    }
}
