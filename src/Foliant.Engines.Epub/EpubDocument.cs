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
/// <para><b>Fixed-reference pagination.</b> Each spine item (chapter HTML) becomes one
/// <see cref="HtmlChapter"/>; pagination, page-count and per-page rendering are delegated to the
/// shared <see cref="HtmlPaginator"/> (laid out once at a fixed reference viewport and split into
/// fixed-height slices; the global page index maps deterministically to a (chapter, local-page) pair).
/// </para>
///
/// <para><see cref="GetTextLayerAsync"/> strips HTML tags via regex and returns one large
/// <see cref="TextRun"/> for the chapter's first page (approximate bounding box) — enough for search
/// (<c>SearchService</c>) and the FTS5 index. Subsequent pages of a chapter return an empty layer;
/// precise per-page text is a future PR.</para>
/// </summary>
internal sealed partial class EpubDocument : IDocument
{
    /// <summary>Reference «page» width in pixels for text-layer bounding boxes and page size
    /// (matches <see cref="HtmlPaginator.ReferenceWidthPx"/>).</summary>
    public const int DefaultPagePxWidth = HtmlPaginator.ReferenceWidthPx;

    /// <summary>Reference «page» height in pixels (matches <see cref="HtmlPaginator.ReferenceHeightPx"/>).</summary>
    public const int DefaultPagePxHeight = HtmlPaginator.ReferenceHeightPx;

    /// <summary>Regex для grubby HTML→text strip: матчит весь тэг (от <c>&lt;</c> до <c>&gt;</c>).
    /// Не валидный HTML parser, но для plain-text-просмотра достаточно. После strip — collapse
    /// whitespace + HTML-entity decode основных entity'ев (&amp;amp; &amp;lt; &amp;gt; &amp;nbsp;).</summary>
    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagsRegex();

    /// <summary>Collapse multiple whitespace chars (including line breaks) to single space.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private readonly IReadOnlyList<EpubLocalTextContentFile> _spine;
    private readonly HtmlPaginator _paginator;

    public DocumentKind Kind => DocumentKind.Epub;

    /// <summary>Total page count across all chapters (Σ of per-chapter slice counts), not the spine
    /// item count.</summary>
    public int PageCount => _paginator.PageCount;

    public DocumentMetadata Metadata { get; }

    private EpubDocument(EpubBook book, IHtmlRenderer renderer)
    {
        _spine = [.. book.ReadingOrder];
        _paginator = new HtmlPaginator(
            renderer,
            [.. _spine.Select(item => new HtmlChapter(
                item.Content ?? string.Empty,
                new EpubResourceResolver(book, item.FilePath)))]);

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
        return Task.Run<IPageRender>(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                HtmlRenderResult result = _paginator.Render(pageIndex, opts.Theme, opts.MaxWidthPx);
                return new EpubPageRender(result.WidthPx, result.HeightPx, result.Stride, result.Bgra32, GetPageSize(pageIndex));
            },
            ct);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        EnsureValidPage(pageIndex);
        ct.ThrowIfCancellationRequested();

        (int chapter, int localPage) = _paginator.Map(pageIndex);

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
