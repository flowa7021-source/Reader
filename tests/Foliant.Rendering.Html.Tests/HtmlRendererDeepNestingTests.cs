using System.Linq;
using FluentAssertions;
using Xunit;

namespace Foliant.Rendering.Html.Tests;

/// <summary>
/// Regression tests for the layout depth guard (<c>LayoutEngine.MaxNestingDepth</c>). Pathologically
/// deep HTML must NOT overflow the stack: the mutually-recursive layout walk would otherwise throw an
/// <b>uncatchable</b> <see cref="System.StackOverflowException"/> that crashes the process — a DoS
/// reachable from a hostile EPUB/FB2/MOBI through eager pagination at document open. The test merely
/// <i>completing</i> (no process crash) is the core proof that the guard bounds the recursion; we
/// additionally assert a valid, non-null result.
/// </summary>
public sealed class HtmlRendererDeepNestingTests
{
    // Comfortably above the observed overflow floor (~2,000–4,000 nested block elements under parallel
    // test stack pressure) so that without the cap this reliably crashes the process, yet small enough
    // to parse quickly. The cap (256) bounds the walk regardless of how deep the DOM is.
    private const int PathologicalDepth = 8_000;

    private static string DeeplyNested(string tag, int depth) =>
        string.Concat(Enumerable.Repeat($"<{tag}>", depth))
        + "deep content"
        + string.Concat(Enumerable.Repeat($"</{tag}>", depth));

    [Theory]
    [InlineData("blockquote")] // block path: LayoutChildren → LayoutBlock → LayoutChildren
    [InlineData("div")]        // block path
    [InlineData("span")]       // inline path: GatherInline → GatherInline
    public void RenderPage_PathologicallyDeepNesting_DoesNotStackOverflow(string tag)
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        HtmlRenderRequest request = RenderTestHelpers.Request(DeeplyNested(tag, PathologicalDepth));

        // Reaching this line (not crashing the process with an uncatchable SOE) is the proof.
        HtmlRenderResult result = renderer.RenderPage(request);

        result.Should().NotBeNull();
        result.Bgra32.Length.Should().Be(result.Stride * result.HeightPx);
    }

    [Fact]
    public void Layout_PathologicallyDeepNesting_CompletesAndIsBounded()
    {
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();

        using HtmlLayout layout = renderer.Layout(RenderTestHelpers.Request(DeeplyNested("blockquote", PathologicalDepth)));

        layout.Should().NotBeNull();
        layout.PageCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ShallowContentBeforeDeepNesting_StillRenders()
    {
        // A normal paragraph (shallow, within the cap) followed by a pathologically deep subtree: the
        // shallow content still renders (ink), while the deep subtree is bounded by the cap rather than
        // crashing. (Content nested *beyond* the cap is intentionally dropped.)
        HtmlRenderer renderer = RenderTestHelpers.NewRenderer();
        string html = "<p>visible paragraph text</p>" + DeeplyNested("div", PathologicalDepth);

        HtmlRenderResult result = renderer.RenderPage(RenderTestHelpers.Request(html));

        RenderTestHelpers.CountNonWhite(result).Should().BeGreaterThan(0);
    }
}
