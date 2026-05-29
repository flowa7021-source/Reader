using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Epub;
using Xunit;

namespace Foliant.Engines.Epub.Tests;

public sealed class EpubDocumentTests : IDisposable
{
    private readonly string _tmpDir;

    public EpubDocumentTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-epub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ───── StripHtmlToPlainText (pure, no I/O) ─────

    [Theory]
    [InlineData("<p>Hello world</p>", "Hello world")]
    [InlineData("<h1>Title</h1><p>Body text.</p>", "Title Body text.")]
    [InlineData("Plain text without tags", "Plain text without tags")]
    [InlineData("", "")]
    [InlineData("<p>Line one</p>\n<p>Line two</p>", "Line one Line two")]
    public void StripHtml_RemovesTags_AndCollapsesWhitespace(string input, string expected)
    {
        EpubDocument.StripHtmlToPlainText(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("&amp;", "&")]
    [InlineData("&lt;tag&gt;", "<tag>")]
    [InlineData("&quot;quoted&quot;", "\"quoted\"")]
    [InlineData("&nbsp;spaced", "spaced")]   // Trim removes leading entity-decoded space.
    [InlineData("a&#39;b", "a'b")]
    [InlineData("c&apos;d", "c'd")]
    public void StripHtml_DecodesCommonEntities(string input, string expected)
    {
        EpubDocument.StripHtmlToPlainText(input).Should().Be(expected);
    }

    [Fact]
    public void StripHtml_RealisticChapter()
    {
        string html = "<h1>Chapter 1</h1><p>It was the <em>best</em> of times, it was the <em>worst</em> of times.</p>";
        EpubDocument.StripHtmlToPlainText(html)
            .Should().Be("Chapter 1 It was the best of times, it was the worst of times.");
    }

    // ───── Open / IDocument surface ─────

    [Fact]
    public void Open_MissingFile_Throws()
    {
        Action act = () => EpubDocument.Open(Path.Combine(_tmpDir, "no-such-file.epub"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_BlankPath_Throws()
    {
        Action act = () => EpubDocument.Open("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Open_MinimalEpub_LoadsMetadata()
    {
        string path = EpubTestFactory.Create(_tmpDir, "Tale of Two Cities", "Charles Dickens",
            "<h1>Chapter 1</h1><p>It was the best of times...</p>",
            "<h1>Chapter 2</h1><p>The Mail.</p>");
        await using var doc = EpubDocument.Open(path);

        doc.Kind.Should().Be(DocumentKind.Epub);
        doc.PageCount.Should().Be(2);
        doc.Metadata.Title.Should().Be("Tale of Two Cities");
        doc.Metadata.Author.Should().Be("Charles Dickens");
    }

    [Fact]
    public async Task GetTextLayerAsync_ReturnsStrippedChapterText()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A",
            "<h1>Chapter 1</h1><p>The quick <em>brown</em> fox.</p>");
        await using var doc = EpubDocument.Open(path);

        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);
        layer.Should().NotBeNull();
        layer!.Runs.Should().ContainSingle();
        layer.Runs[0].Text.Should().Contain("Chapter 1");
        layer.Runs[0].Text.Should().Contain("quick brown fox");
        layer.Runs[0].Text.Should().NotContain("<");  // tags stripped
        layer.Runs[0].Text.Should().NotContain(">");
    }

    [Fact]
    public async Task GetTextLayerAsync_OutOfRange_Throws()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>One</p>");
        await using var doc = EpubDocument.Open(path);

        Func<Task> act = async () => await doc.GetTextLayerAsync(5, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RenderPageAsync_ReturnsBlankWhiteBitmap()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        await using var doc = EpubDocument.Open(path);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        render.WidthPx.Should().Be(EpubDocument.DefaultPagePxWidth);
        render.HeightPx.Should().Be(EpubDocument.DefaultPagePxHeight);
        render.Stride.Should().Be(EpubDocument.DefaultPagePxWidth * 4);
        // First pixel must be white (BGRA = FF FF FF FF).
        var span = render.Bgra32.Span;
        span[0].Should().Be(0xFF);
        span[1].Should().Be(0xFF);
        span[2].Should().Be(0xFF);
        span[3].Should().Be(0xFF);
    }

    [Fact]
    public async Task RenderPageAsync_HonorsMaxWidthOpts()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        await using var doc = EpubDocument.Open(path);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0, MaxWidthPx: 400), CancellationToken.None);
        render.WidthPx.Should().Be(400);
    }

    [Fact]
    public async Task GetEditor_GetForms_GetSignatures_AllNull()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        await using var doc = EpubDocument.Open(path);

        doc.GetEditor().Should().BeNull();
        doc.GetForms().Should().BeNull();
        doc.GetSignatures().Should().BeNull();
    }
}
