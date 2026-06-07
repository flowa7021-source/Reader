using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Foliant.Domain;
using Foliant.Rendering.Html;
using VersOne.Epub;

namespace Foliant.Engines.Epub;

/// <summary>
/// EPUB document backed by VersOne.Epub, rendered to real visual pages through the pure-managed
/// <see cref="IHtmlRenderer"/> (<c>Foliant.Rendering.Html</c>): AngleSharp parse → block/inline
/// layout with word-wrap → SixLabors paint → BGRA32. No native dependencies (cross-platform; tested
/// on Linux).
///
/// <para><b>Fixed-reference pagination.</b> Each spine item (chapter HTML) is laid out at a fixed
/// reference viewport (<see cref="DefaultPagePxWidth"/> × <see cref="DefaultPagePxHeight"/>,
/// <see cref="ReferenceMargins"/>, <see cref="BaseFontSizePx"/>, scale 1.0) and split into one or
/// more fixed-height page slices. The page count per chapter is computed eagerly at
/// <see cref="Open(string, IHtmlRenderer)"/> and is scale-invariant — every length (font, margins,
/// content width) scales together, so the slice count is independent of the render scale. The
/// document's global page index therefore maps deterministically to a (chapter, local-page) pair.
/// </para>
///
/// <para><see cref="GetTextLayerAsync"/> strips HTML tags via regex and returns one large
/// <see cref="TextRun"/> for the chapter's first page (approximate bounding box) — enough for search
/// (<c>SearchService</c>) and the FTS5 index. Subsequent pages of a chapter return an empty layer;
/// precise per-page text is a future PR.</para>
/// </summary>
internal sealed partial class EpubDocument : IDocument
{
    /// <summary>Reference «page» width in pixels for the render canvas and text-layer bounding boxes.</summary>
    public const int DefaultPagePxWidth = 800;

    /// <summary>Reference «page» height in pixels.</summary>
    public const int DefaultPagePxHeight = 1200;

    /// <summary>Reference root font size in CSS pixels.</summary>
    private const double BaseFontSizePx = 18.0;

    /// <summary>Lower clamp on the derived render scale (guards pathological MaxWidthPx values).</summary>
    private const double MinScale = 0.05;

    /// <summary>Upper clamp on the derived render scale.</summary>
    private const double MaxScale = 8.0;

    /// <summary>Per-chapter page-count cap, guarding against pathological layouts.</summary>
    private const int MaxPagesPerChapter = 5000;

    /// <summary>The fixed content insets used for the reference pagination geometry.</summary>
    private static readonly HtmlMargins ReferenceMargins = new(40, 48, 40, 48);

    /// <summary>Regex для grubby HTML→text strip: матчит весь тэг (от <c>&lt;</c> до <c>&gt;</c>).
    /// Не валидный HTML parser, но для plain-text-просмотра достаточно. После strip — collapse
    /// whitespace + HTML-entity decode основных entity'ев (&amp;amp; &amp;lt; &amp;gt; &amp;nbsp;).</summary>
    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagsRegex();

    /// <summary>Collapse multiple whitespace chars (including line breaks) to single space.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private readonly EpubBook _book;
    private readonly IReadOnlyList<EpubLocalTextContentFile> _spine;
    private readonly IHtmlRenderer _renderer;

    /// <summary>Number of fixed-height page slices each spine chapter spans (always &#8805; 1).</summary>
    private readonly int[] _pagesInChapter;

    /// <summary>Prefix sums of <see cref="_pagesInChapter"/>: <c>_cumulativePages[i]</c> is the global
    /// index of chapter <c>i</c>'s first page. Length = chapter count + 1; last entry = total pages.</summary>
    private readonly int[] _cumulativePages;

    public DocumentKind Kind => DocumentKind.Epub;

    /// <summary>Total page count across all chapters (Σ of per-chapter slice counts), not the spine
    /// item count.</summary>
    public int PageCount => _cumulativePages[^1];

    public DocumentMetadata Metadata { get; }

    private EpubDocument(EpubBook book, IHtmlRenderer renderer)
    {
        _book = book;
        _renderer = renderer;
        _spine = [.. book.ReadingOrder];

        _pagesInChapter = new int[_spine.Count];
        _cumulativePages = new int[_spine.Count + 1];
        for (int i = 0; i < _spine.Count; i++)
        {
            _pagesInChapter[i] = ComputeChapterPages(i);
            _cumulativePages[i + 1] = _cumulativePages[i] + _pagesInChapter[i];
        }

        Metadata = new DocumentMetadata(
            Title: book.Title,
            Author: book.Author,
            Subject: null,
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>());
    }

    /// <summary>Opens an EPUB at <paramref name="path"/> and eagerly paginates it against the
    /// supplied renderer.</summary>
    /// <param name="path">Filesystem path to the <c>.epub</c> archive.</param>
    /// <param name="renderer">The shared HTML renderer used for pagination and page painting.</param>
    /// <returns>The loaded document.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is null.</exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    public static EpubDocument Open(string path, IHtmlRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("EPUB file not found.", path);
        }

        EpubBook book = EpubReader.ReadBook(path);
        return new EpubDocument(book, renderer);
    }

    public PageSize GetPageSize(int pageIndex)
    {
        EnsureValidPage(pageIndex);
        // EPUB не имеет «point» geometry; используем пиксели как pt (1:1 на 72 DPI).
        return new PageSize(DefaultPagePxWidth, DefaultPagePxHeight);
    }

    public Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(opts);
        EnsureValidPage(pageIndex);
        return Task.Run<IPageRender>(() => RenderPageCore(pageIndex, opts, ct), ct);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        EnsureValidPage(pageIndex);
        ct.ThrowIfCancellationRequested();

        (int chapter, int localPage) = GlobalToLocal(pageIndex);

        // The chapter's text is indexed once, on its first page; later pages of the same chapter
        // return an empty layer (precise per-page text is a future PR).
        if (localPage > 0)
        {
            return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, []));
        }

        string html = _spine[chapter].Content ?? string.Empty;
        string text = StripHtmlToPlainText(html);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, []));
        }

        // Один большой run на всю «страницу» — bounding box покрывает viewport.
        // Координаты в pt (= px при 72 DPI).
        var run = new TextRun(text, 0, 0, DefaultPagePxWidth, DefaultPagePxHeight);
        return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, [run]));
    }

    public IDocumentEditor? GetEditor() => null;
    public IFormController? GetForms() => null;
    public ISignatureController? GetSignatures() => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Internal — strip HTML tags + decode basic entities + collapse whitespace.
    /// Public for unit-testing without an EPUB asset.</summary>
    internal static string StripHtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        // Strip tags.
        string stripped = HtmlTagsRegex().Replace(html, " ");

        // Decode common entities. Limited set — не полный HTML5, но покрывает 99 % практики.
        var sb = new StringBuilder(stripped);
        sb.Replace("&nbsp;", " ");
        sb.Replace("&amp;", "&");
        sb.Replace("&lt;", "<");
        sb.Replace("&gt;", ">");
        sb.Replace("&quot;", "\"");
        sb.Replace("&#39;", "'");
        sb.Replace("&apos;", "'");

        // Collapse whitespace.
        return WhitespaceRegex().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Lays out chapter <paramref name="chapter"/> at the reference viewport and returns its
    /// (clamped) page-slice count. The layout is disposed immediately — chapter layouts are not
    /// retained in memory.</summary>
    private int ComputeChapterPages(int chapter)
    {
        var request = new HtmlRenderRequest(
            Html: _spine[chapter].Content ?? string.Empty,
            Resources: new EpubResourceResolver(_book, _spine[chapter].FilePath),
            Viewport: ReferenceViewport(pageIndexInChapter: 0),
            Theme: RenderTheme.Original);

        using HtmlLayout layout = _renderer.Layout(request);
        return Math.Clamp(layout.PageCount, 1, MaxPagesPerChapter);
    }

    private static HtmlViewport ReferenceViewport(int pageIndexInChapter) => new(
        ContentWidthPx: DefaultPagePxWidth,
        PageHeightPx: DefaultPagePxHeight,
        Margins: ReferenceMargins,
        BaseFontSizePx: BaseFontSizePx,
        ScalePx: 1.0,
        PageIndexInChapter: pageIndexInChapter);

    /// <summary>Maps a global page index to its (chapter, local-page-within-chapter) pair via the
    /// prefix-sum table.</summary>
    private (int Chapter, int LocalPage) GlobalToLocal(int globalIndex)
    {
        // Binary search for the chapter whose [start, start+pages) range contains globalIndex.
        int lo = 0;
        int hi = _spine.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_cumulativePages[mid] <= globalIndex)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (lo, globalIndex - _cumulativePages[lo]);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Robustness contract: a render failure (malformed chapter HTML, image decode, etc.) must degrade to a blank page rather than crash the reader. OperationCanceledException is rethrown so cancellation still propagates.")]
    private EpubPageRender RenderPageCore(int globalIndex, RenderOptions opts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        (int chapter, int localPage) = GlobalToLocal(globalIndex);

        // Derive the render scale from MaxWidthPx only; Zoom is intentionally a no-op for EPUB today
        // (zoom-driven reflow is a deferred follow-up). At scale 1 (the main reading view, which
        // passes no MaxWidthPx) the viewport equals the reference geometry, so local-page alignment
        // with the eager pagination is exact.
        double scale = opts.MaxWidthPx is { } mw && mw > 0 ? (double)mw / DefaultPagePxWidth : 1.0;
        scale = Math.Clamp(scale, MinScale, MaxScale);

        int widthPx = Math.Max(1, (int)Math.Round(DefaultPagePxWidth * scale));
        int heightPx = Math.Max(1, (int)Math.Round(DefaultPagePxHeight * scale));

        try
        {
            var viewport = new HtmlViewport(
                ContentWidthPx: widthPx,
                PageHeightPx: heightPx,
                Margins: ScaleMargins(ReferenceMargins, scale),
                BaseFontSizePx: BaseFontSizePx,
                ScalePx: scale,
                PageIndexInChapter: localPage);

            var request = new HtmlRenderRequest(
                Html: _spine[chapter].Content ?? string.Empty,
                Resources: new EpubResourceResolver(_book, _spine[chapter].FilePath),
                Viewport: viewport,
                Theme: opts.Theme);

            HtmlRenderResult result = _renderer.RenderPage(request);
            return new EpubPageRender(result.WidthPx, result.HeightPx, result.Stride, result.Bgra32, GetPageSize(globalIndex));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Best-effort fallback: a blank white bitmap at the scaled size.
            return BlankPage(widthPx, heightPx, GetPageSize(globalIndex));
        }
    }

    private static EpubPageRender BlankPage(int widthPx, int heightPx, PageSize pageSize)
    {
        int stride = widthPx * 4;
        byte[] buffer = new byte[stride * heightPx];
        // BGRA32 white = 0xFF FF FF FF.
        Array.Fill(buffer, (byte)0xFF);
        return new EpubPageRender(widthPx, heightPx, stride, buffer, pageSize);
    }

    private static HtmlMargins ScaleMargins(HtmlMargins margins, double scale) => new(
        (int)Math.Round(margins.Left * scale),
        (int)Math.Round(margins.Top * scale),
        (int)Math.Round(margins.Right * scale),
        (int)Math.Round(margins.Bottom * scale));

    private void EnsureValidPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
    }
}

/// <summary>Render-обёртка над BGRA32-bitmap'ом из <see cref="IHtmlRenderer"/>. Owner = caller;
/// Dispose — no-op (no native handle).</summary>
internal sealed class EpubPageRender(int widthPx, int heightPx, int stride, ReadOnlyMemory<byte> bgra32, PageSize pageSize) : IPageRender
{
    public int WidthPx => widthPx;
    public int HeightPx => heightPx;
    public int Stride => stride;
    public ReadOnlyMemory<byte> Bgra32 => bgra32;
    public PageSize PageSize => pageSize;

    public void Dispose()
    {
        // No native resources.
    }
}
