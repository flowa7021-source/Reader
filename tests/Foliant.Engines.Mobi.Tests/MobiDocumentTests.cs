using FluentAssertions;
using Foliant.Domain;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Mobi.Tests;

public sealed class MobiDocumentTests
{
    private static readonly IHtmlRenderer Renderer = new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    [Fact]
    public void Parse_SingleTextRecord_ProducesOnePageWithStrippedText()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><p>Hello MOBI world</p></body></html>", title: "My Book");

        var doc = MobiDocument.Parse(mobi, Renderer);

        doc.Kind.Should().Be(DocumentKind.Mobi);
        doc.PageCount.Should().Be(1);
        doc.Metadata.Title.Should().Be("My Book");
    }

    [Fact]
    public async Task GetTextLayer_ReturnsStrippedHtmlText()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><h1>Title</h1><p>Body&nbsp;text</p></body></html>");

        var doc = MobiDocument.Parse(mobi, Renderer);
        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);

        layer.Should().NotBeNull();
        layer!.Runs.Should().ContainSingle();
        layer.Runs[0].Text.Should().Contain("Title").And.Contain("Body").And.Contain("text");
        layer.Runs[0].Text.Should().NotContain("<");
    }

    [Fact]
    public void Parse_RecordsWithoutMarkers_ConcatenateToOneChapter()
    {
        // Text records are arbitrary splits of one HTML stream: with no page-break markers they
        // concatenate into a single short chapter → one page.
        byte[] mobi = MobiTestFactory.Build(
            ["<p>Chapter one</p>", "<p>Chapter two</p>", "<p>Chapter three</p>"]);

        var doc = MobiDocument.Parse(mobi, Renderer);

        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void Parse_PageBreakMarkerBetweenChapters_SplitsIntoMultiplePages()
    {
        // Two records joined by a MOBI page-break marker → two chapters → at least two pages.
        byte[] mobi = MobiTestFactory.Build(
            ["<html><body><p>First chapter body.</p>", "<mbp:pagebreak/><p>Second chapter body.</p></body></html>"]);

        var doc = MobiDocument.Parse(mobi, Renderer);

        doc.PageCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Parse_LongSingleRecord_PaginatesToMultiplePages()
    {
        // One long chapter (no markers) must paginate into several fixed-height slices.
        string longBody = "<html><body>"
            + string.Concat(Enumerable.Repeat("<p>The quick brown fox jumps over the lazy dog.</p>", 400))
            + "</body></html>";
        byte[] mobi = MobiTestFactory.Build(longBody);

        var doc = MobiDocument.Parse(mobi, Renderer);

        doc.PageCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task RenderPage_RendersNonBlankContent()
    {
        // A MOBI with real text records renders ink (non-white pixels), not a blank canvas.
        string body = "<html><body><h1>Chapter</h1><p>"
            + string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 30))
            + "</p></body></html>";
        byte[] mobi = MobiTestFactory.Build(body);
        var doc = MobiDocument.Parse(mobi, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        render.WidthPx.Should().Be(MobiDocument.DefaultPagePxWidth);
        render.HeightPx.Should().Be(MobiDocument.DefaultPagePxHeight);
        render.Stride.Should().Be(MobiDocument.DefaultPagePxWidth * 4);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
        CountNonWhitePixels(render).Should().BeGreaterThan(0, "rendered text should produce non-white pixels");
    }

    [Fact]
    public async Task RenderPage_HonorsMaxWidthOpts()
    {
        byte[] mobi = MobiTestFactory.Build("<p>x</p>");
        var doc = MobiDocument.Parse(mobi, Renderer);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0, MaxWidthPx: 400), CancellationToken.None);

        render.WidthPx.Should().Be(400);
    }

    [Fact]
    public void Parse_TooSmall_Throws()
    {
        var act = () => MobiDocument.Parse(new byte[10], Renderer);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetPageSize_OutOfRange_Throws()
    {
        var doc = MobiDocument.Parse(MobiTestFactory.Build("<p>x</p>"), Renderer);

        var act = () => doc.GetPageSize(5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

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
