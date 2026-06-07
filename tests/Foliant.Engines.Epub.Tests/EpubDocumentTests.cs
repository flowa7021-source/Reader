using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Epub;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Foliant.Engines.Epub.Tests;

public sealed class EpubDocumentTests : IDisposable
{
    private static readonly IHtmlRenderer Renderer = new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

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
        Action act = () => EpubDocument.Open(Path.Combine(_tmpDir, "no-such-file.epub"), Renderer);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_BlankPath_Throws()
    {
        Action act = () => EpubDocument.Open("  ", Renderer);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Open_MinimalEpub_LoadsMetadata()
    {
        string path = EpubTestFactory.Create(_tmpDir, "Tale of Two Cities", "Charles Dickens",
            "<h1>Chapter 1</h1><p>It was the best of times...</p>",
            "<h1>Chapter 2</h1><p>The Mail.</p>");
        await using var doc = EpubDocument.Open(path, Renderer);

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
        await using var doc = EpubDocument.Open(path, Renderer);

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
        await using var doc = EpubDocument.Open(path, Renderer);

        Func<Task> act = async () => await doc.GetTextLayerAsync(5, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetTextLayerAsync_SecondPageOfChapter_IsEmpty()
    {
        // A long single chapter spans multiple pages; only page 0 carries the chapter text.
        string longHtml = string.Concat(Enumerable.Repeat("<p>The quick brown fox jumps over the lazy dog.</p>", 400));
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", longHtml);
        await using var doc = EpubDocument.Open(path, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);

        var first = await doc.GetTextLayerAsync(0, CancellationToken.None);
        first!.Runs.Should().ContainSingle();
        first.Runs[0].Text.Should().Contain("quick brown fox");

        var second = await doc.GetTextLayerAsync(1, CancellationToken.None);
        second!.Runs.Should().BeEmpty();
    }

    // ───── Pagination ─────

    [Fact]
    public async Task PageCount_PaginatesLongChapter()
    {
        // One chapter with very long content paginates into several fixed-height slices.
        string longHtml = string.Concat(Enumerable.Repeat("<p>The quick brown fox jumps over the lazy dog.</p>", 400));
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", longHtml);
        await using var doc = EpubDocument.Open(path, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task PageCount_TwoShortChapters_IsTwo()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A",
            "<h1>Ch1</h1><p>Short.</p>",
            "<h1>Ch2</h1><p>Also short.</p>");
        await using var doc = EpubDocument.Open(path, Renderer);

        doc.PageCount.Should().Be(2);
    }

    // ───── RenderPageAsync (real content) ─────

    [Fact]
    public async Task RenderPageAsync_RendersNonBlankContent()
    {
        string body = "<h1>Chapter</h1><p>" + string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 30)) + "</p>";
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", body);
        await using var doc = EpubDocument.Open(path, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        render.WidthPx.Should().Be(EpubDocument.DefaultPagePxWidth);
        render.HeightPx.Should().Be(EpubDocument.DefaultPagePxHeight);
        render.Stride.Should().Be(EpubDocument.DefaultPagePxWidth * 4);
        CountNonWhitePixels(render).Should().BeGreaterThan(0, "rendered text should produce non-white pixels");
    }

    [Fact]
    public async Task RenderPageAsync_EmptyChapter_IsAllWhite()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "");
        await using var doc = EpubDocument.Open(path, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        render.WidthPx.Should().Be(EpubDocument.DefaultPagePxWidth);
        render.HeightPx.Should().Be(EpubDocument.DefaultPagePxHeight);
        CountNonWhitePixels(render).Should().Be(0);
    }

    [Fact]
    public async Task RenderPageAsync_HonorsMaxWidthOpts()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        await using var doc = EpubDocument.Open(path, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0, MaxWidthPx: 400), CancellationToken.None);
        render.WidthPx.Should().Be(400);
    }

    [Fact]
    public async Task RenderPageAsync_SecondPageDiffersFromFirst()
    {
        string longHtml = string.Concat(Enumerable.Repeat("<p>The quick brown fox jumps over the lazy dog.</p>", 400));
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", longHtml);
        await using var doc = EpubDocument.Open(path, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);

        using var page0 = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        using var page1 = await doc.RenderPageAsync(1, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        page0.Bgra32.ToArray().Should().NotEqual(page1.Bgra32.ToArray());
    }

    // ───── Images ─────

    [Fact]
    public void ResourceResolver_ResolvesChapterRelativeImage()
    {
        byte[] png = SolidPng(8, 8, new Rgba32(10, 20, 200, 255));
        string path = EpubTestFactory.Create(_tmpDir, "T", "A",
            new[] { new EpubImage("img/pic.png", "image/png", png) },
            "<p>before</p><img src=\"img/pic.png\"/><p>after</p>");

        var book = VersOne.Epub.EpubReader.ReadBook(path);
        // Spine item FilePath is OEBPS/chapter1.xhtml → chapter-relative "img/pic.png" resolves.
        var resolver = new EpubResourceResolver(book, book.ReadingOrder[0].FilePath);

        resolver.TryResolveImage("img/pic.png", out var bytes).Should().BeTrue();
        bytes.ToArray().Should().Equal(png);
    }

    [Fact]
    public async Task RenderPageAsync_WithImage_ProducesNonWhitePixels()
    {
        // A blue image on an otherwise short chapter should colour the page.
        byte[] png = SolidPng(64, 64, new Rgba32(10, 20, 200, 255));
        string path = EpubTestFactory.Create(_tmpDir, "T", "A",
            new[] { new EpubImage("img/pic.png", "image/png", png) },
            "<img src=\"img/pic.png\"/>");
        await using var doc = EpubDocument.Open(path, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        CountNonWhitePixels(render).Should().BeGreaterThan(0, "the embedded image should paint non-white pixels");
    }

    [Fact]
    public async Task GetEditor_GetForms_GetSignatures_AllNull()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        await using var doc = EpubDocument.Open(path, Renderer);

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

    private static byte[] SolidPng(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
