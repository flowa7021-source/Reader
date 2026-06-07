using System.Diagnostics.CodeAnalysis;
using Foliant.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.Rendering.Html;

/// <summary>One reflowable unit of a document (an EPUB spine item, an FB2 section, the whole MOBI
/// text) plus the resolver that supplies its <c>&lt;img&gt;</c> bytes.</summary>
/// <param name="Html">The chapter's HTML (or HTML-ish) markup.</param>
/// <param name="Resources">Image/resource resolver bound to this chapter (use
/// <see cref="NullResourceResolver.Instance"/> when the format has no resolvable images yet).</param>
public sealed record HtmlChapter(string Html, IResourceResolver Resources);

/// <summary>
/// Fixed-reference, scale-invariant pagination shared by all HTML-backed engines (EPUB/FB2/MOBI).
/// Each <see cref="HtmlChapter"/> is laid out once at a fixed reference viewport
/// (<see cref="ReferenceWidthPx"/> × <see cref="ReferenceHeightPx"/>, scale 1.0) and split into one or
/// more fixed-height page slices; the per-chapter counts are summed into a prefix-sum table so a flat
/// global page index maps deterministically to a (chapter, local-page) pair.
///
/// <para>Pagination is computed eagerly in the constructor at scale 1.0 and is the single source of
/// truth for <see cref="PageCount"/> — which an <see cref="IDocument"/> exposes once. Because every
/// length (font, margins, content width) scales together, the slice count is independent of the render
/// scale, so the main reading view (no <c>MaxWidthPx</c> ⇒ scale 1.0) aligns exactly with the eager
/// pagination. Thumbnails render at a smaller scale where independent pixel rounding can shift a
/// chapter's internal slice count by ±1 — so a thumbnail of a chapter's last page may occasionally be
/// blank. That is a cosmetic edge inside the deferred zoom-reflow scope; it never affects
/// <see cref="PageCount"/> or the main view.</para>
/// </summary>
public sealed class HtmlPaginator
{
    /// <summary>Reference page width in pixels (content canvas at scale 1.0).</summary>
    public const int ReferenceWidthPx = 800;

    /// <summary>Reference page height in pixels (the fixed slice height at scale 1.0).</summary>
    public const int ReferenceHeightPx = 1200;

    private const double BaseFontSizePx = 18.0;
    private const double MinScale = 0.05;
    private const double MaxScale = 8.0;
    private const int MaxPagesPerChapter = 5000;
    private static readonly HtmlMargins ReferenceMargins = new(40, 48, 40, 48);

    private readonly IHtmlRenderer _renderer;
    private readonly IReadOnlyList<HtmlChapter> _chapters;
    private readonly int[] _pagesInChapter;
    private readonly int[] _cumulativePages;

    /// <summary>Paginates <paramref name="chapters"/> eagerly against <paramref name="renderer"/>.</summary>
    /// <param name="renderer">The shared HTML renderer used for layout (pagination) and painting.</param>
    /// <param name="chapters">The document's chapters in reading order (may be empty).</param>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> or <paramref name="chapters"/> is null.</exception>
    public HtmlPaginator(IHtmlRenderer renderer, IReadOnlyList<HtmlChapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(chapters);
        _renderer = renderer;
        _chapters = chapters;

        _pagesInChapter = new int[chapters.Count];
        _cumulativePages = new int[chapters.Count + 1];
        for (int i = 0; i < chapters.Count; i++)
        {
            _pagesInChapter[i] = ComputeChapterPages(i);
            _cumulativePages[i + 1] = _cumulativePages[i] + _pagesInChapter[i];
        }
    }

    /// <summary>Total page count across all chapters (Σ of per-chapter slice counts).</summary>
    public int PageCount => _cumulativePages[^1];

    /// <summary>Number of chapters.</summary>
    public int ChapterCount => _chapters.Count;

    /// <summary>Maps a global page index to its (chapter, local-page-within-chapter) pair. The index is
    /// clamped defensively to <c>[0, PageCount-1]</c>.</summary>
    /// <param name="globalIndex">The flat page index.</param>
    /// <returns>The owning chapter and the 0-based page within it.</returns>
    public (int Chapter, int LocalPage) Map(int globalIndex)
    {
        if (_chapters.Count == 0)
        {
            return (0, 0);
        }

        int idx = Math.Clamp(globalIndex, 0, Math.Max(0, PageCount - 1));

        // Upper-bound binary search for the chapter whose [start, start+pages) range contains idx.
        int lo = 0;
        int hi = _chapters.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_cumulativePages[mid] <= idx)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (lo, idx - _cumulativePages[lo]);
    }

    /// <summary>Renders the page slice at <paramref name="globalIndex"/> to a BGRA32 result. The render
    /// scale is derived from <paramref name="maxWidthPx"/> only (zoom-driven reflow is a deferred
    /// follow-up); at scale 1.0 (no <paramref name="maxWidthPx"/>) the viewport equals the reference
    /// geometry, so the painted slice aligns exactly with the eager pagination. Never throws on content:
    /// a layout/paint failure degrades to a blank page at the scaled size.</summary>
    /// <param name="globalIndex">The flat page index to render.</param>
    /// <param name="theme">The render theme to apply to the final buffer.</param>
    /// <param name="maxWidthPx">Optional output width cap (e.g. thumbnails); null ⇒ reference scale.</param>
    /// <returns>The rendered page (BGRA32).</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Robustness contract: a render failure (malformed HTML, image decode, etc.) must degrade to a blank page rather than crash the reader.")]
    public HtmlRenderResult Render(int globalIndex, RenderTheme theme, int? maxWidthPx)
    {
        (int chapter, int localPage) = Map(globalIndex);

        double scale = maxWidthPx is { } mw && mw > 0 ? (double)mw / ReferenceWidthPx : 1.0;
        scale = Math.Clamp(scale, MinScale, MaxScale);

        int widthPx = Math.Max(1, (int)Math.Round(ReferenceWidthPx * scale));
        int heightPx = Math.Max(1, (int)Math.Round(ReferenceHeightPx * scale));

        if (_chapters.Count == 0)
        {
            return BlankResult(widthPx, heightPx, theme);
        }

        try
        {
            var viewport = new HtmlViewport(
                ContentWidthPx: widthPx,
                PageHeightPx: heightPx,
                Margins: ScaleMargins(ReferenceMargins, scale),
                BaseFontSizePx: BaseFontSizePx,
                ScalePx: scale,
                PageIndexInChapter: localPage);

            var request = new HtmlRenderRequest(_chapters[chapter].Html, _chapters[chapter].Resources, viewport, theme);
            return _renderer.RenderPage(request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return BlankResult(widthPx, heightPx, theme);
        }
    }

    private int ComputeChapterPages(int chapter)
    {
        var request = new HtmlRenderRequest(
            _chapters[chapter].Html,
            _chapters[chapter].Resources,
            ReferenceViewport(pageIndexInChapter: 0),
            RenderTheme.Original);

        using HtmlLayout layout = _renderer.Layout(request);
        return Math.Clamp(layout.PageCount, 1, MaxPagesPerChapter);
    }

    private static HtmlViewport ReferenceViewport(int pageIndexInChapter) => new(
        ContentWidthPx: ReferenceWidthPx,
        PageHeightPx: ReferenceHeightPx,
        Margins: ReferenceMargins,
        BaseFontSizePx: BaseFontSizePx,
        ScalePx: 1.0,
        PageIndexInChapter: pageIndexInChapter);

    private static HtmlMargins ScaleMargins(HtmlMargins margins, double scale) => new(
        (int)Math.Round(margins.Left * scale),
        (int)Math.Round(margins.Top * scale),
        (int)Math.Round(margins.Right * scale),
        (int)Math.Round(margins.Bottom * scale));

    private static HtmlRenderResult BlankResult(int widthPx, int heightPx, RenderTheme theme)
    {
        int stride = widthPx * 4;
        byte[] buffer = new byte[stride * heightPx];
        using (var image = new Image<Bgra32>(widthPx, heightPx, new Bgra32(255, 255, 255, 255)))
        {
            image.CopyPixelDataTo(buffer);
        }

        RenderColorMap.ApplyTheme(buffer, theme);
        return new HtmlRenderResult(widthPx, heightPx, stride, buffer, 1);
    }
}
