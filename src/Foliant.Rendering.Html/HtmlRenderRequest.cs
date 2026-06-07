using Foliant.Domain;

namespace Foliant.Rendering.Html;

/// <summary>
/// A complete, self-contained request to lay out (and optionally paint) one chapter of HTML.
/// Value-in / value-out: the renderer reads everything it needs from here and returns a buffer;
/// it performs no file or network I/O of its own (images come via <see cref="Resources"/>).
/// </summary>
/// <param name="Html">The chapter HTML. Malformed markup is tolerated; <see langword="null"/> is rejected.</param>
/// <param name="Resources">Resolver for <c>&lt;img&gt;</c> sources. Use <see cref="NullResourceResolver.Instance"/> for text-only.</param>
/// <param name="Viewport">The fixed paint surface (size, margins, base font, scale, page index).</param>
/// <param name="Theme">View theme applied to the final BGRA32 buffer via <see cref="RenderColorMap.ApplyTheme"/>.</param>
public sealed record HtmlRenderRequest(
    string Html,
    IResourceResolver Resources,
    HtmlViewport Viewport,
    RenderTheme Theme);
