namespace Foliant.Rendering.Html;

/// <summary>
/// The fixed paint surface for one chapter: how wide the page is, how tall a single page slice
/// is, the margins to carve out, the base (root) font size, a global zoom factor, and which page
/// slice within the chapter to paint.
/// </summary>
/// <param name="ContentWidthPx">Full bitmap width in pixels (content column = this minus left/right margins).</param>
/// <param name="PageHeightPx">Height of a single page slice in pixels; also the pagination stride.</param>
/// <param name="Margins">Content insets (already in device pixels).</param>
/// <param name="BaseFontSizePx">Root font size in CSS pixels (before <see cref="ScalePx"/>); typical body text is this size.</param>
/// <param name="ScalePx">Global zoom multiplier applied to every CSS pixel length (font sizes, margins, indents).</param>
/// <param name="PageIndexInChapter">Zero-based index of the page slice to paint in <c>RenderPage</c>.</param>
public readonly record struct HtmlViewport(
    int ContentWidthPx,
    int PageHeightPx,
    HtmlMargins Margins,
    double BaseFontSizePx,
    double ScalePx,
    int PageIndexInChapter)
{
    /// <summary>
    /// A conventional A4-ish 800&#215;1200 viewport with <see cref="HtmlMargins.Default"/> margins,
    /// 16&#160;px base font and 1.0 scale, painting the first page. Mirrors the EPUB engine's
    /// 800&#215;1200 blank-canvas convention so the spike output matches the eventual wiring.
    /// </summary>
    public static HtmlViewport Default { get; } = new(
        ContentWidthPx: 800,
        PageHeightPx: 1200,
        Margins: HtmlMargins.Default,
        BaseFontSizePx: 16.0,
        ScalePx: 1.0,
        PageIndexInChapter: 0);
}
