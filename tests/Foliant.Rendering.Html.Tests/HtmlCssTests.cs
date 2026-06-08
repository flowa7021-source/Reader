using FluentAssertions;
using Xunit;

namespace Foliant.Rendering.Html.Tests;

/// <summary>
/// Author-CSS cascade tests driven through the public renderer API: <c>&lt;style&gt;</c> blocks and
/// linked stylesheets (resolved via <see cref="StubResourceResolver"/>) layered over the user-agent
/// defaults and under inline <c>style</c> attributes, with selector specificity, source order and
/// <c>!important</c> resolved per the CSS cascade. Colour/size/family assertions inspect the resolved
/// <see cref="TextDrawCommand"/> rather than pixels, so they are exact and platform-independent.
/// </summary>
public sealed class HtmlCssTests
{
    private static TextDrawCommand FirstText(HtmlLayout layout)
        => layout.Commands.OfType<TextDrawCommand>().First();

    private static HtmlLayout LayoutOf(string html, StubResourceResolver? resources = null)
        => RenderTestHelpers.NewRenderer().Layout(RenderTestHelpers.Request(html, resources: resources));

    // ───── selectors apply over UA defaults ─────

    [Fact]
    public void StyleBlock_TagSelector_AppliesColor()
    {
        using HtmlLayout layout = LayoutOf("<style>p{color:#ff0000}</style><p>colored body words</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void StyleBlock_ClassSelector_AppliesColor()
    {
        using HtmlLayout layout = LayoutOf("<style>.warn{color:red}</style><p class=\"warn\">warning words here</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void StyleBlock_IdSelector_AppliesColor()
    {
        using HtmlLayout layout = LayoutOf("<style>#lead{color:red}</style><p id=\"lead\">lead paragraph text</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void StyleBlock_DescendantSelector_AppliesColor()
    {
        using HtmlLayout layout = LayoutOf("<style>div p{color:red}</style><div><p>nested paragraph words</p></div>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    // ───── cascade: specificity & source order ─────

    [Fact]
    public void Specificity_IdBeatsClassBeatsTag()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>p{color:green} .c{color:blue} #i{color:red}</style><p id=\"i\" class=\"c\">priority words</p>");

        // id (1,0,0) > class (0,1,0) > tag (0,0,1): red wins.
        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void Specificity_ClassBeatsTag()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>p{color:blue} .c{color:red}</style><p class=\"c\">class over tag words</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void SourceOrder_LaterRuleWins_AtEqualSpecificity()
    {
        using HtmlLayout layout = LayoutOf("<style>p{color:blue} p{color:red}</style><p>later rule wins</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    // ───── cascade: inline vs author, !important ─────

    [Fact]
    public void InlineStyle_BeatsAuthorNormal()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>p{color:blue}</style><p style=\"color:red\">inline beats author</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void AuthorImportant_BeatsInlineNormal()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>p{color:red !important}</style><p style=\"color:blue\">important author wins</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void InlineImportant_BeatsAuthorImportant()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>p{color:blue !important}</style><p style=\"color:red !important\">inline important wins</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void AuthorImportant_BeatsHigherSpecificityNormal()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>#i{color:blue} p{color:red !important}</style><p id=\"i\">important beats specificity</p>");

        // !important lifts the low-specificity tag rule above the normal id rule.
        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    // ───── linked stylesheets via the resolver ─────

    [Fact]
    public void LinkedStylesheet_ResolvedViaResolver_AppliesColor()
    {
        var resources = new StubResourceResolver().AddCss("styles/main.css", ".red{color:red}");
        using HtmlLayout layout = LayoutOf(
            "<head><link rel=\"stylesheet\" href=\"styles/main.css\"/></head><body><p class=\"red\">linked css words</p></body>",
            resources);

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void LinkedStylesheet_AndStyleBlock_CascadeInDocumentOrder()
    {
        // The <style> block appears after the <link>, so at equal specificity it wins.
        var resources = new StubResourceResolver().AddCss("a.css", "p{color:blue}");
        using HtmlLayout layout = LayoutOf(
            "<head><link rel=\"stylesheet\" href=\"a.css\"/><style>p{color:red}</style></head><body><p>order words</p></body>",
            resources);

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void LinkedStylesheet_UnresolvedHref_DegradesToDefault()
    {
        var resources = new StubResourceResolver(); // resolves nothing.
        using HtmlLayout layout = LayoutOf(
            "<head><link rel=\"stylesheet\" href=\"missing.css\"/></head><body><p>default colored text</p></body>",
            resources);

        // No CSS resolved → default black (neither red nor blue dominant).
        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeFalse();
    }

    // ───── font properties from author CSS ─────

    [Fact]
    public void FontSize_FromAuthorCss_EnlargesFont()
    {
        using HtmlLayout small = LayoutOf("<style>p{font-size:12px}</style><p>sized words</p>");
        using HtmlLayout large = LayoutOf("<style>p{font-size:48px}</style><p>sized words</p>");

        FirstText(large).Font.Size.Should().BeGreaterThan(FirstText(small).Font.Size);
    }

    [Fact]
    public void FontFamily_FromAuthorCss_SelectsMonospace()
    {
        using HtmlLayout layout = LayoutOf("<style>p{font-family:monospace}</style><p>code-like words</p>");

        FirstText(layout).Font.Name.Should().Contain("Mono");
    }

    [Fact]
    public void TextAlign_FromAuthorCss_CentersText()
    {
        using HtmlLayout centered = LayoutOf("<style>p{text-align:center}</style><p>centered</p>");
        using HtmlLayout left = LayoutOf("<style>p{text-align:left}</style><p>centered</p>");

        FirstText(centered).X.Should().BeGreaterThan(FirstText(left).X);
    }

    [Fact]
    public void FontWeight_FromAuthorCss_BoldEmitsAtLeastAsMuchInk()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        const string text = "the quick brown fox jumps over the lazy dog again";

        int regular = RenderTestHelpers.CountNonWhite(renderer.RenderPage(
            RenderTestHelpers.Request($"<style>p{{font-weight:normal}}</style><p>{text}</p>")));
        int bold = RenderTestHelpers.CountNonWhite(renderer.RenderPage(
            RenderTestHelpers.Request($"<style>p{{font-weight:bold}}</style><p>{text}</p>")));

        bold.Should().BeGreaterThanOrEqualTo(regular);
    }

    // ───── display:none & metadata elements ─────

    [Fact]
    public void DisplayNone_FromAuthorCss_HidesElementAndSubtree()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>.hidden{display:none}</style><p class=\"hidden\">SECRET</p><p>visible</p>");

        var words = layout.Commands.OfType<TextDrawCommand>().Select(c => c.Text).ToList();
        words.Should().Contain("visible");
        words.Should().NotContain("SECRET");
    }

    [Fact]
    public void DisplayNone_OnAncestor_HidesNestedContent()
    {
        using HtmlLayout layout = LayoutOf(
            "<style>.gone{display:none}</style><div class=\"gone\"><p>nested SECRET text</p></div><p>shown</p>");

        var words = layout.Commands.OfType<TextDrawCommand>().Select(c => c.Text).ToList();
        words.Should().Contain("shown");
        words.Should().NotContain("SECRET");
    }

    [Fact]
    public void StyleBlockText_IsNeverPaintedAsBodyText()
    {
        using HtmlLayout layout = LayoutOf("<style>p{color:red;font-size:20px}</style><p>only this paints</p>");

        var words = layout.Commands.OfType<TextDrawCommand>().Select(c => c.Text).ToList();
        words.Should().NotContain(w => w.Contains("color", StringComparison.Ordinal) || w.Contains('{'));
        words.Should().Contain("only");
    }

    // ───── box properties ─────

    [Fact]
    public void Margin_FromAuthorCss_IncreasesContentHeight()
    {
        using HtmlLayout tight = LayoutOf("<style>div{margin-top:0;margin-bottom:0}</style><div>a</div><div>b</div>");
        using HtmlLayout spaced = LayoutOf("<style>div{margin-top:120px;margin-bottom:0}</style><div>a</div><div>b</div>");

        spaced.TotalContentHeightPx.Should().BeGreaterThan(tight.TotalContentHeightPx);
    }

    [Fact]
    public void DisplayBlock_FromAuthorCss_BreaksInlineFlow()
    {
        // A span is inline by default; display:block forces each onto its own row.
        using HtmlLayout inline = LayoutOf("<p><span>one</span><span>two</span></p>");
        using HtmlLayout blocked = LayoutOf("<style>span{display:block}</style><p><span>one</span><span>two</span></p>");

        int inlineRows = inline.Commands.OfType<TextDrawCommand>().Select(c => c.Y).Distinct().Count();
        int blockedRows = blocked.Commands.OfType<TextDrawCommand>().Select(c => c.Y).Distinct().Count();

        blockedRows.Should().BeGreaterThan(inlineRows);
    }

    // ───── robustness ─────

    [Fact]
    public void MalformedCss_DegradesGracefully_AndStillRendersText()
    {
        using HtmlLayout layout = LayoutOf("<style>p{{{ color: ;;; broken @@@ </style><p>still renders</p>");

        layout.Commands.OfType<TextDrawCommand>().Select(c => c.Text).Should().Contain("still");
    }

    [Fact]
    public void EmptyStyleBlock_IsHarmless()
    {
        using HtmlLayout layout = LayoutOf("<style></style><p>plain text body</p>");

        layout.Commands.OfType<TextDrawCommand>().Should().NotBeEmpty();
    }

    [Fact]
    public void RgbFunctionalColor_FromAuthorCss_AppliesRed()
    {
        using HtmlLayout layout = LayoutOf("<style>p{color:rgb(220, 0, 0)}</style><p>functional rgb words</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void HslFunctionalColor_FromAuthorCss_AppliesRed()
    {
        using HtmlLayout layout = LayoutOf("<style>p{color:hsl(0, 100%, 45%)}</style><p>functional hsl words</p>");

        RenderTestHelpers.IsRedDominant(FirstText(layout).Color).Should().BeTrue();
    }

    [Fact]
    public void RenderPage_LinkedCss_ProducesRedPixels()
    {
        var resources = new StubResourceResolver().AddCss("s.css", "p{color:#e00000}");
        HtmlRenderResult result = RenderTestHelpers.NewRenderer().RenderPage(RenderTestHelpers.Request(
            "<head><link rel=\"stylesheet\" href=\"s.css\"/></head><body><p>RED RED RED RED RED RED</p></body>",
            resources: resources));

        RenderTestHelpers.CountRedDominant(result).Should().BeGreaterThan(0);
    }
}
