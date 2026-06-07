namespace Foliant.Rendering.Html;

/// <summary>
/// Page content margins, in device pixels, applied <em>before</em> the viewport
/// <see cref="HtmlViewport.ScalePx"/> is taken into account (i.e. these are already-scaled
/// pixel insets carved out of the painted bitmap). The text/content column is laid out inside
/// the rectangle <c>[Left, Top] .. [ContentWidthPx - Right, PageHeightPx - Bottom]</c>.
/// </summary>
/// <param name="Left">Left inset in pixels.</param>
/// <param name="Top">Top inset in pixels.</param>
/// <param name="Right">Right inset in pixels.</param>
/// <param name="Bottom">Bottom inset in pixels.</param>
public readonly record struct HtmlMargins(int Left, int Top, int Right, int Bottom)
{
    /// <summary>A sensible default margin (24&#160;px on every side).</summary>
    public static HtmlMargins Default { get; } = new(24, 24, 24, 24);
}
