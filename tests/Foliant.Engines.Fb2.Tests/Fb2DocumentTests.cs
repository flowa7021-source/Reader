using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Fb2;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Fb2.Tests;

public sealed class Fb2DocumentTests : IDisposable
{
    private static readonly IHtmlRenderer Renderer = new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    private readonly string _tmpDir;

    public Fb2DocumentTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-fb2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ───── CollapseWhitespace (pure, no I/O) ─────

    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("line\n\n\nbreaks", "line breaks")]
    [InlineData("tabs\t\there", "tabs here")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CollapseWhitespace_NormalisesAllRunsToSingleSpace(string input, string expected)
    {
        Fb2Document.CollapseWhitespaceForTest(input).Should().Be(expected);
    }

    // ───── Open / IDocument surface ─────

    [Fact]
    public void Open_MissingFile_Throws()
    {
        Action act = () => Fb2Document.Open(Path.Combine(_tmpDir, "nope.fb2"), Renderer);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_BlankPath_Throws()
    {
        Action act = () => Fb2Document.Open("  ", Renderer);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_NotXml_ThrowsInvalidData()
    {
        string path = Path.Combine(_tmpDir, "not-xml.fb2");
        File.WriteAllText(path, "This is plain text, not XML.");
        Action act = () => Fb2Document.Open(path, Renderer);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Open_XmlButWrongRoot_Throws()
    {
        string path = Path.Combine(_tmpDir, "wrong-root.fb2");
        File.WriteAllText(path, "<?xml version=\"1.0\"?><root/>");
        Action act = () => Fb2Document.Open(path, Renderer);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Open_MinimalFb2_LoadsMetadataAndPages()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "Война и мир", "Лев", "Толстой",
            "Все счастливые семьи похожи друг на друга.",
            "Каждая несчастливая семья несчастлива по-своему.");
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.Kind.Should().Be(DocumentKind.Fb2);
        doc.PageCount.Should().Be(2);
        doc.Metadata.Title.Should().Be("Война и мир");
        doc.Metadata.Author.Should().Be("Лев Толстой");
    }

    [Fact]
    public async Task GetTextLayerAsync_ReturnsSectionText()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L",
            "The quick brown fox.");
        await using var doc = Fb2Document.Open(path, Renderer);

        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);
        layer.Should().NotBeNull();
        layer!.Runs.Should().ContainSingle();
        layer.Runs[0].Text.Should().Contain("Section 1");      // title
        layer.Runs[0].Text.Should().Contain("quick brown fox"); // paragraph
    }

    [Fact]
    public async Task BodyOnly_NoSections_TreatedAsOnePage()
    {
        string path = Fb2TestFactory.CreateBodyOnly(_tmpDir, "Short", "Just one paragraph of body text.");
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.PageCount.Should().Be(1);
        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);
        layer!.Runs[0].Text.Should().Contain("Just one paragraph");
    }

    [Fact]
    public async Task GetTextLayerAsync_OutOfRange_Throws()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path, Renderer);

        Func<Task> act = async () => await doc.GetTextLayerAsync(5, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetTextLayerAsync_SecondPageOfChapter_IsEmpty()
    {
        // A single very long section spans multiple pages; only page 0 carries the chapter text.
        string longPara = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 400));
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", longPara);
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);

        var first = await doc.GetTextLayerAsync(0, CancellationToken.None);
        first!.Runs.Should().ContainSingle();
        first.Runs[0].Text.Should().Contain("quick brown fox");

        var second = await doc.GetTextLayerAsync(1, CancellationToken.None);
        second!.Runs.Should().BeEmpty();
    }

    // ───── Pagination ─────

    [Fact]
    public async Task PageCount_MultiSection_AtLeastSectionCount()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L",
            "Section one body.",
            "Section two body.",
            "Section three body.");
        await using var doc = Fb2Document.Open(path, Renderer);

        // Each non-empty section is a chapter spanning ≥ 1 page.
        doc.PageCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task PageCount_PaginatesLongSection()
    {
        // One section with very long content paginates into several fixed-height slices.
        string longPara = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 400));
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", longPara);
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);
    }

    // ───── RenderPageAsync (real content) ─────

    [Fact]
    public async Task RenderPageAsync_RendersNonBlankContent()
    {
        string body = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 30));
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", body);
        await using var doc = Fb2Document.Open(path, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        render.WidthPx.Should().Be(Fb2Document.DefaultPagePxWidth);
        render.HeightPx.Should().Be(Fb2Document.DefaultPagePxHeight);
        render.Stride.Should().Be(Fb2Document.DefaultPagePxWidth * 4);
        CountNonWhitePixels(render).Should().BeGreaterThan(0, "rendered text should produce non-white pixels");
    }

    [Fact]
    public async Task RenderPageAsync_EmptyDocument_IsAllWhite()
    {
        // A FictionBook with an empty body → one empty chapter → blank white page.
        string path = Fb2TestFactory.CreateEmptyBody(_tmpDir, "Empty");
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.PageCount.Should().Be(1);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        render.WidthPx.Should().Be(Fb2Document.DefaultPagePxWidth);
        render.HeightPx.Should().Be(Fb2Document.DefaultPagePxHeight);
        CountNonWhitePixels(render).Should().Be(0);
    }

    [Fact]
    public async Task RenderPageAsync_HonorsMaxWidthOpts()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0, MaxWidthPx: 400), CancellationToken.None);
        render.WidthPx.Should().Be(400);
    }

    [Fact]
    public async Task RenderPageAsync_SecondPageDiffersFromFirst()
    {
        string longPara = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 400));
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", longPara);
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);

        using var page0 = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        using var page1 = await doc.RenderPageAsync(1, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        page0.Bgra32.ToArray().Should().NotEqual(page1.Bgra32.ToArray());
    }

    [Fact]
    public async Task GetEditor_GetForms_GetSignatures_AllNull()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path, Renderer);

        doc.GetEditor().Should().BeNull();
        doc.GetForms().Should().BeNull();
        doc.GetSignatures().Should().BeNull();
    }

    // ───── helpers ─────

    /// <summary>Counts BGRA32 pixels that are not pure white (FF FF FF, alpha ignored).</summary>
    private static int CountNonWhitePixels(IPageRender render)
    {
        ReadOnlySpan<byte> span = render.Bgra32.Span;
        int count = 0;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            if (span[i] != 0xFF || span[i + 1] != 0xFF || span[i + 2] != 0xFF)
            {
                count++;
            }
        }

        return count;
    }
}
