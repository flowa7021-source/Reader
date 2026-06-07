using FluentAssertions;
using SixLabors.ImageSharp;
using Xunit;

namespace Foliant.Rendering.Html.Tests;

public sealed class HtmlImageTests
{
    [Fact]
    public void RenderPage_ResolvedImage_IsComposited()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        byte[] png = RenderTestHelpers.SolidPng(120, 80, Color.Black);
        var resources = new StubResourceResolver().Add("pic.png", png);

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<img src=\"pic.png\">", resources: resources));

        RenderTestHelpers.CountNonWhite(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ResolvedRedImage_ProducesRedDominantPixels()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        byte[] png = RenderTestHelpers.SolidPng(100, 60, Color.Red);
        var resources = new StubResourceResolver().Add("red.png", png);

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<img src=\"red.png\">", resources: resources));

        RenderTestHelpers.CountRedDominant(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_UnresolvedImage_IsSkippedAndStaysWhite()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        // NullResourceResolver resolves nothing; the <img> must be skipped silently.
        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<img src=\"missing.png\">"));

        RenderTestHelpers.CountNonWhite(result).Should().Be(0);
    }

    [Fact]
    public void RenderPage_CorruptImageBytes_DoesNotThrowAndStaysWhite()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        var resources = new StubResourceResolver().Add("bad.png", [0x00, 0x01, 0x02, 0x03, 0x04]);

        HtmlRenderResult result = renderer.RenderPage(
            RenderTestHelpers.Request("<img src=\"bad.png\">", resources: resources));

        RenderTestHelpers.CountNonWhite(result).Should().Be(0);
    }

    [Fact]
    public void Layout_WideImage_IsScaledDownToContentWidth()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        // 4000px image into an 800px viewport (≈752px content) must be scaled down.
        byte[] png = RenderTestHelpers.SolidPng(4000, 1000, Color.Blue);
        var resources = new StubResourceResolver().Add("big.png", png);

        using HtmlLayout layout = renderer.Layout(
            RenderTestHelpers.Request("<img src=\"big.png\">", resources: resources));

        ImageDrawCommand image = layout.Commands.OfType<ImageDrawCommand>().Single();
        image.Width.Should().BeLessThan(800);
        image.Width.Should().BeGreaterThan(0);
        // Aspect ratio preserved (within rounding): width/height ≈ 4.
        (image.Width / image.DestHeight).Should().BeApproximately(4f, 0.2f);
    }

    [Fact]
    public void Layout_ImageAndText_BothEmitCommands()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        byte[] png = RenderTestHelpers.SolidPng(50, 50, Color.Green);
        var resources = new StubResourceResolver().Add("a.png", png);

        using HtmlLayout layout = renderer.Layout(
            RenderTestHelpers.Request("<p>caption</p><img src=\"a.png\">", resources: resources));

        layout.Commands.OfType<TextDrawCommand>().Should().NotBeEmpty();
        layout.Commands.OfType<ImageDrawCommand>().Should().ContainSingle();
    }
}
