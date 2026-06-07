using FluentAssertions;
using Xunit;

namespace Foliant.Rendering.Html.Tests;

public sealed class HtmlLayoutTests
{
    [Fact]
    public void Layout_LongParagraphInNarrowColumn_WrapsToMultipleLines()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string longText = string.Concat(Enumerable.Repeat("word ", 60));
        var narrow = HtmlViewport.Default with { ContentWidthPx = 160 };

        using HtmlLayout layout = renderer.Layout(RenderTestHelpers.Request($"<p>{longText}</p>", narrow));

        // A single line is ~ base-size * 1.3 ≈ 21 px; wrapping many words must exceed several lines.
        layout.TotalContentHeightPx.Should().BeGreaterThan(100);
        layout.Commands.OfType<TextDrawCommand>().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Layout_NarrowerColumn_ProducesTallerContentThanWideColumn()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string text = string.Concat(Enumerable.Repeat("alpha ", 80));

        var wide = HtmlViewport.Default with { ContentWidthPx = 800 };
        var narrow = HtmlViewport.Default with { ContentWidthPx = 200 };

        using HtmlLayout wideLayout = renderer.Layout(RenderTestHelpers.Request($"<p>{text}</p>", wide));
        using HtmlLayout narrowLayout = renderer.Layout(RenderTestHelpers.Request($"<p>{text}</p>", narrow));

        narrowLayout.TotalContentHeightPx.Should().BeGreaterThan(wideLayout.TotalContentHeightPx);
    }

    [Fact]
    public void Layout_DistinctYPositions_IndicateMultipleLines()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string longText = string.Concat(Enumerable.Repeat(" web ", 80));
        var narrow = HtmlViewport.Default with { ContentWidthPx = 160 };

        using HtmlLayout layout = renderer.Layout(RenderTestHelpers.Request($"<p>{longText}</p>", narrow));

        int distinctRows = layout.Commands.OfType<TextDrawCommand>().Select(c => c.Y).Distinct().Count();
        distinctRows.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Layout_EmptyBody_HasNoCommandsAndSinglePage()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout layout = renderer.Layout(RenderTestHelpers.Request("<body></body>"));

        layout.Commands.Should().BeEmpty();
        layout.PageCount.Should().Be(1);
    }

    [Fact]
    public void Layout_SmallerPageHeight_IncreasesPageCount()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string content = string.Concat(Enumerable.Repeat("<p>paragraph of text that takes vertical space</p>", 40));

        var tall = HtmlViewport.Default with { PageHeightPx = 2000 };
        var shortPage = HtmlViewport.Default with { PageHeightPx = 300 };

        using HtmlLayout tallLayout = renderer.Layout(RenderTestHelpers.Request(content, tall));
        using HtmlLayout shortLayout = renderer.Layout(RenderTestHelpers.Request(content, shortPage));

        shortLayout.PageCount.Should().BeGreaterThan(tallLayout.PageCount);
    }

    [Fact]
    public void Layout_PageCount_IsAtLeastOneEvenWhenEmpty()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout layout = renderer.Layout(RenderTestHelpers.Request(string.Empty));

        layout.PageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void RenderPage_ResultPageCount_MatchesLayoutPageCount()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string content = string.Concat(Enumerable.Repeat("<p>matching page counts across both APIs</p>", 30));
        var shortPage = HtmlViewport.Default with { PageHeightPx = 250 };

        using HtmlLayout layout = renderer.Layout(RenderTestHelpers.Request(content, shortPage));
        HtmlRenderResult page = renderer.RenderPage(RenderTestHelpers.Request(content, shortPage));

        page.PageCountInChapter.Should().Be(layout.PageCount);
        page.PageCountInChapter.Should().BeGreaterThan(1);
    }

    [Fact]
    public void RenderPage_SecondPage_DiffersFromFirst_WhenContentSpansTwoPages()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string content = string.Concat(Enumerable.Repeat("<p>line of content for pagination across pages</p>", 30));

        // Choose a page height that guarantees at least two pages.
        var page0Viewport = HtmlViewport.Default with { PageHeightPx = 300, PageIndexInChapter = 0 };
        var page1Viewport = HtmlViewport.Default with { PageHeightPx = 300, PageIndexInChapter = 1 };

        HtmlRenderResult page0 = renderer.RenderPage(RenderTestHelpers.Request(content, page0Viewport));
        HtmlRenderResult page1 = renderer.RenderPage(RenderTestHelpers.Request(content, page1Viewport));

        page0.PageCountInChapter.Should().BeGreaterThan(1);
        page1.Bgra32.Should().NotEqual(page0.Bgra32);
    }

    [Fact]
    public void RenderPage_PageBeyondContent_IsAllWhite()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        var viewport = HtmlViewport.Default with { PageHeightPx = 400, PageIndexInChapter = 50 };

        HtmlRenderResult result = renderer.RenderPage(RenderTestHelpers.Request("<p>tiny</p>", viewport));

        RenderTestHelpers.CountNonWhite(result).Should().Be(0);
    }

    [Fact]
    public void Layout_OrderedList_EmitsNumericMarkers()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout layout = renderer.Layout(
            RenderTestHelpers.Request("<ol><li>first</li><li>second</li><li>third</li></ol>"));

        var texts = layout.Commands.OfType<TextDrawCommand>().Select(c => c.Text).ToList();
        texts.Should().Contain("1.");
        texts.Should().Contain("2.");
        texts.Should().Contain("3.");
    }

    [Fact]
    public void Layout_UnorderedList_EmitsBulletMarkers()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout layout = renderer.Layout(
            RenderTestHelpers.Request("<ul><li>alpha</li><li>beta</li></ul>"));

        layout.Commands.OfType<TextDrawCommand>().Count(c => c.Text == "•").Should().Be(2);
    }

    [Fact]
    public void Layout_CenteredText_OffsetsFragmentsRightOfLeftMargin()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout centered = renderer.Layout(
            RenderTestHelpers.Request("<p style=\"text-align:center\">centered</p>"));
        using HtmlLayout left = renderer.Layout(
            RenderTestHelpers.Request("<p style=\"text-align:left\">centered</p>"));

        float centeredX = centered.Commands.OfType<TextDrawCommand>().First().X;
        float leftX = left.Commands.OfType<TextDrawCommand>().First().X;

        centeredX.Should().BeGreaterThan(leftX);
    }

    [Fact]
    public void Layout_BrTag_AdvancesToNewLine()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout withBreak = renderer.Layout(RenderTestHelpers.Request("<p>top<br>bottom</p>"));

        var rows = withBreak.Commands.OfType<TextDrawCommand>().Select(c => c.Y).Distinct().ToList();
        rows.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
