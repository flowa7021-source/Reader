using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Rendering.Html.Tests;

public sealed class HtmlRendererTests
{
    [Fact]
    public void RenderPage_NonEmptyHtml_ProducesInk()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<h1>Hi</h1><p>body text that should paint plenty of ink</p>"));

        RenderTestHelpers.CountNonWhite(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_HasExpectedDimensionsAndStride()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        var viewport = HtmlViewport.Default with { ContentWidthPx = 640, PageHeightPx = 900 };

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<p>hello</p>", viewport));

        result.WidthPx.Should().Be(640);
        result.HeightPx.Should().Be(900);
        result.Stride.Should().Be(640 * 4);
        result.Bgra32.Length.Should().Be(900 * 640 * 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  \r\n")]
    [InlineData("<body></body>")]
    [InlineData("<html><body>   </body></html>")]
    public void RenderPage_EmptyOrWhitespaceHtml_IsAllWhite(string html)
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult result = renderer.RenderPage(RenderTestHelpers.Request(html));

        RenderTestHelpers.CountNonWhite(result).Should().Be(0);
    }

    [Fact]
    public void RenderPage_MoreParagraphs_ProduceAtLeastAsMuchInk()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        const string oneParagraph = "<p>The quick brown fox jumps over the lazy dog.</p>";
        string manyParagraphs = string.Concat(Enumerable.Repeat("<p>The quick brown fox jumps over the lazy dog.</p>", 5));

        int oneInk = RenderTestHelpers.CountNonWhite(renderer.RenderPage(RenderTestHelpers.Request(oneParagraph)));
        int manyInk = RenderTestHelpers.CountNonWhite(renderer.RenderPage(RenderTestHelpers.Request(manyParagraphs)));

        manyInk.Should().BeGreaterThanOrEqualTo(oneInk);
        manyInk.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_BoldText_ProducesAtLeastAsMuchInkAsRegular()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        const string text = "The quick brown fox jumps over the lazy dog repeatedly";

        int regular = RenderTestHelpers.CountNonWhite(
            renderer.RenderPage(RenderTestHelpers.Request($"<p>{text}</p>")));
        int bold = RenderTestHelpers.CountNonWhite(
            renderer.RenderPage(RenderTestHelpers.Request($"<p style=\"font-weight:bold\">{text}</p>")));

        bold.Should().BeGreaterThanOrEqualTo(regular);
    }

    [Fact]
    public void RenderPage_LargerFontSize_ProducesAtLeastAsMuchInk()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        const string text = "Ink scales with size";

        int small = RenderTestHelpers.CountNonWhite(
            renderer.RenderPage(RenderTestHelpers.Request($"<p style=\"font-size:12px\">{text}</p>")));
        int large = RenderTestHelpers.CountNonWhite(
            renderer.RenderPage(RenderTestHelpers.Request($"<p style=\"font-size:48px\">{text}</p>")));

        large.Should().BeGreaterThanOrEqualTo(small);
        large.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_RedColoredText_YieldsRedDominantPixels()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<p style=\"color:#ff0000\">RED RED RED RED RED RED RED</p>"));

        RenderTestHelpers.CountRedDominant(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_NamedRedColor_YieldsRedDominantPixels()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<p style=\"color:red\">crimson words everywhere here</p>"));

        RenderTestHelpers.CountRedDominant(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_DarkTheme_InvertsBackgroundToDark()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult original = renderer.RenderPage(
            RenderTestHelpers.Request("<p>themed</p>", theme: RenderTheme.Original));
        HtmlRenderResult dark = renderer.RenderPage(
            RenderTestHelpers.Request("<p>themed</p>", theme: RenderTheme.Dark));

        // A top-left pixel is background (white in Original; inverted to near-black in Dark).
        (byte B, byte G, byte R) light = RenderTestHelpers.PixelAt(original, 1, 1);
        (byte B, byte G, byte R) darkPixel = RenderTestHelpers.PixelAt(dark, 1, 1);

        light.Should().Be(((byte)255, (byte)255, (byte)255));
        darkPixel.B.Should().BeLessThan(40);
        darkPixel.G.Should().BeLessThan(40);
        darkPixel.R.Should().BeLessThan(40);
    }

    [Fact]
    public void RenderPage_HighContrastTheme_ChangesBuffer()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult original = renderer.RenderPage(
            RenderTestHelpers.Request("<p>contrast</p>", theme: RenderTheme.Original));
        HtmlRenderResult highContrast = renderer.RenderPage(
            RenderTestHelpers.Request("<p>contrast</p>", theme: RenderTheme.HighContrast));

        highContrast.Bgra32.Should().NotEqual(original.Bgra32);
    }

    [Fact]
    public void RenderPage_SameRequestTwice_IsByteIdentical()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        HtmlRenderRequest request = RenderTestHelpers.Request("<h2>Deterministic</h2><p>same bytes every time, please</p>");

        HtmlRenderResult first = renderer.RenderPage(request);
        HtmlRenderResult second = renderer.RenderPage(request);

        second.Bgra32.Should().Equal(first.Bgra32);
    }

    [Fact]
    public void RenderPage_MalformedHtml_DoesNotThrowAndProducesInk()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<p>unclosed <b>bold <i>and italic <span>broken markup"));

        RenderTestHelpers.CountNonWhite(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ZeroDimensionViewport_StillProducesValidBitmap()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        var viewport = HtmlViewport.Default with { ContentWidthPx = 0, PageHeightPx = 0 };

        HtmlRenderResult result = renderer.RenderPage(RenderTestHelpers.Request("<p>x</p>", viewport));

        result.WidthPx.Should().BeGreaterThanOrEqualTo(1);
        result.HeightPx.Should().BeGreaterThanOrEqualTo(1);
        result.Bgra32.Length.Should().Be(result.HeightPx * result.Stride);
    }

    [Fact]
    public void Constructor_NullFonts_Throws()
    {
        Action act = () => _ = new HtmlRenderer(null!, NullLogger<HtmlRenderer>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => _ = new HtmlRenderer(RenderTestHelpers.Fonts, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenderPage_NullRequest_Throws()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        Action act = () => renderer.RenderPage(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenderPage_NullHtml_Throws()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        var request = new HtmlRenderRequest(null!, NullResourceResolver.Instance, HtmlViewport.Default, RenderTheme.Original);

        Action act = () => renderer.RenderPage(request);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Layout_NullRequest_Throws()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        Action act = () => renderer.Layout(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
