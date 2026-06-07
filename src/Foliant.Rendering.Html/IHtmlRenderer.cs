namespace Foliant.Rendering.Html;

/// <summary>
/// Renders HTML into BGRA32 page-slice bitmaps. Pure value-in / value-out: the implementation owns
/// no I/O (images flow in via <see cref="HtmlRenderRequest.Resources"/>) and is safe to call
/// concurrently.
/// </summary>
public interface IHtmlRenderer
{
    /// <summary>Lays out and paints a single page slice (the one named by the request's viewport).</summary>
    /// <param name="request">The render request (HTML, resources, viewport, theme).</param>
    /// <returns>The painted BGRA32 result plus the chapter's total page count.</returns>
    HtmlRenderResult RenderPage(HtmlRenderRequest request);

    /// <summary>
    /// Lays out the chapter without painting — for pagination / page-count queries. The returned
    /// <see cref="HtmlLayout"/> owns any decoded images; dispose it when done.
    /// </summary>
    /// <param name="request">The render request (HTML, resources, viewport).</param>
    /// <returns>The draw commands, total content height and page count.</returns>
    HtmlLayout Layout(HtmlRenderRequest request);
}
