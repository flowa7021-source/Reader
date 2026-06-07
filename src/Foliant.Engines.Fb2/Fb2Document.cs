using System.Text;
using System.Xml.Linq;
using Foliant.Domain;
using Foliant.Rendering.Html;

namespace Foliant.Engines.Fb2;

/// <summary>
/// FB2 (FictionBook 2.0) document, rendered to real visual pages through the pure-managed
/// <see cref="IHtmlRenderer"/> (<c>Foliant.Rendering.Html</c>): each flattened
/// <c>&lt;section&gt;</c> is converted to an HTML fragment by <see cref="Fb2HtmlConverter"/>, then
/// laid out and painted to BGRA32 — no native dependencies (cross-platform; tested on Linux).
///
/// <para><b>Chapter model.</b> Sections are flattened in pre-order: a chapter is produced for each
/// <c>&lt;section&gt;</c> with non-empty direct content, descending into nested sections. A
/// <c>&lt;body&gt;</c> without sections becomes one chapter from its text; a document with no
/// renderable content becomes one empty chapter (so <see cref="PageCount"/> &#8805; 1). Each chapter
/// carries both its HTML (for the renderer) and its plain text (for the text layer).</para>
///
/// <para><b>Pagination</b> is delegated to the shared <see cref="HtmlPaginator"/> (fixed reference
/// viewport, scale-invariant, eager page counts) — the same engine EPUB/MOBI use; a long section
/// therefore paginates into several pages and the global page index maps deterministically to a
/// (chapter, local-page) pair.</para>
///
/// <para><see cref="GetTextLayerAsync"/> returns the chapter's plain text as one large
/// <see cref="TextRun"/> on the chapter's first page (approximate bounding box) — enough for search
/// (<c>SearchService</c>) and the FTS5 index. Subsequent pages of a chapter return an empty layer.</para>
/// </summary>
internal sealed class Fb2Document : IDocument
{
    /// <summary>Reference «page» width in pixels for text-layer bounding boxes and page size
    /// (matches <see cref="HtmlPaginator.ReferenceWidthPx"/>).</summary>
    public const int DefaultPagePxWidth = HtmlPaginator.ReferenceWidthPx;

    /// <summary>Reference «page» height in pixels (matches <see cref="HtmlPaginator.ReferenceHeightPx"/>).</summary>
    public const int DefaultPagePxHeight = HtmlPaginator.ReferenceHeightPx;

    /// <summary>FB2 namespace URI. Все элементы FB2-документа в этом namespace'е.</summary>
    private static readonly XNamespace Fb2Ns = "http://www.gribuser.ru/xml/fictionbook/2.0";

    private readonly HtmlPaginator _paginator;

    /// <summary>Per-chapter plain text (the text-layer source); parallel to the paginator's chapters.</summary>
    private readonly string[] _chapterTexts;

    public DocumentKind Kind => DocumentKind.Fb2;

    /// <summary>Total page count across all chapters (Σ of per-chapter slice counts), not the chapter
    /// count.</summary>
    public int PageCount => _paginator.PageCount;

    public DocumentMetadata Metadata { get; }

    private Fb2Document(IReadOnlyList<Fb2Chapter> chapters, DocumentMetadata metadata, IHtmlRenderer renderer)
    {
        Metadata = metadata;
        _chapterTexts = [.. chapters.Select(c => c.Text)];
        _paginator = new HtmlPaginator(
            renderer,
            [.. chapters.Select(c => new HtmlChapter(c.Html, NullResourceResolver.Instance))]);
    }

    /// <summary>Opens an FB2 file at <paramref name="path"/> and eagerly paginates it against the
    /// supplied renderer.</summary>
    /// <param name="path">Filesystem path to the <c>.fb2</c> file.</param>
    /// <param name="renderer">The shared HTML renderer used for pagination and page painting.</param>
    /// <returns>The loaded document.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is null.</exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not valid FB2 / XML.</exception>
    public static Fb2Document Open(string path, IHtmlRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("FB2 file not found.", path);
        }

        XDocument xml;
        try
        {
            xml = XDocument.Load(path, LoadOptions.None);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException($"Not a valid FB2 / XML file: {path}", ex);
        }

        XElement? root = xml.Root;
        if (root is null || root.Name != Fb2Ns + "FictionBook")
        {
            throw new InvalidDataException($"Root element is not <FictionBook> in FB2 namespace: {path}");
        }

        var chapters = new List<Fb2Chapter>();
        foreach (XElement body in root.Elements(Fb2Ns + "body"))
        {
            CollectChaptersFromBody(body, chapters);
        }

        if (chapters.Count == 0)
        {
            // Документ без renderable content — одна пустая «глава», чтобы PageCount был ≥ 1.
            chapters.Add(new Fb2Chapter(string.Empty, string.Empty));
        }

        DocumentMetadata metadata = ExtractMetadata(root);
        return new Fb2Document(chapters, metadata, renderer);
    }

    private static void CollectChaptersFromBody(XElement body, List<Fb2Chapter> chapters)
    {
        // Top-level body: ищем <section> детей. Если нет — body становится одной главой
        // со всем своим текстом/HTML.
        var sections = body.Elements(Fb2Ns + "section").ToList();
        if (sections.Count == 0)
        {
            string bodyText = CollapseWhitespace(ExtractTextFromElement(body));
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                chapters.Add(new Fb2Chapter(Fb2HtmlConverter.ConvertSection(body), bodyText));
            }

            return;
        }

        foreach (XElement section in sections)
        {
            CollectChaptersFromSection(section, chapters);
        }
    }

    /// <summary>Рекурсивно flatten'ит секцию в плоский список глав. Прямой контент
    /// (title/subtitle/p/...) становится одной главой; каждая nested-секция — следующей.</summary>
    private static void CollectChaptersFromSection(XElement section, List<Fb2Chapter> chapters)
    {
        string directText = ExtractDirectTextFromSection(section);
        if (!string.IsNullOrWhiteSpace(directText))
        {
            chapters.Add(new Fb2Chapter(Fb2HtmlConverter.ConvertSection(section), directText));
        }

        foreach (XElement nested in section.Elements(Fb2Ns + "section"))
        {
            CollectChaptersFromSection(nested, chapters);
        }
    }

    /// <summary>Достаёт текст из прямых блок-детей секции (<c>&lt;title&gt;</c>, <c>&lt;subtitle&gt;</c>,
    /// <c>&lt;p&gt;</c>, <c>&lt;epigraph&gt;</c>, <c>&lt;cite&gt;</c>) — не рекурсивно по секциям; nested
    /// sections обрабатываются отдельной итерацией.</summary>
    private static string ExtractDirectTextFromSection(XElement section)
    {
        var sb = new StringBuilder();
        foreach (XElement child in section.Elements())
        {
            if (child.Name == Fb2Ns + "section")
            {
                continue; // nested — обрабатывается рекурсивно вызывающим
            }

            if (child.Name == Fb2Ns + "title" || child.Name == Fb2Ns + "subtitle" || child.Name == Fb2Ns + "p"
                || child.Name == Fb2Ns + "epigraph" || child.Name == Fb2Ns + "cite")
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(ExtractTextFromElement(child));
            }
        }

        return CollapseWhitespace(sb.ToString());
    }

    private static string ExtractTextFromElement(XElement element)
    {
        // .Value собирает весь рекурсивный текст; FB2-теги inline (<emphasis>, <strong>) — этого хватает.
        return element.Value ?? string.Empty;
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool prevWs = false;
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWs)
                {
                    sb.Append(' ');
                    prevWs = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevWs = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static DocumentMetadata ExtractMetadata(XElement root)
    {
        XElement? titleInfo = root.Element(Fb2Ns + "description")?.Element(Fb2Ns + "title-info");
        string? title = titleInfo?.Element(Fb2Ns + "book-title")?.Value?.Trim();
        XElement? firstAuthor = titleInfo?.Element(Fb2Ns + "author");
        string? author = null;
        if (firstAuthor is not null)
        {
            string first = firstAuthor.Element(Fb2Ns + "first-name")?.Value?.Trim() ?? string.Empty;
            string last = firstAuthor.Element(Fb2Ns + "last-name")?.Value?.Trim() ?? string.Empty;
            string composed = $"{first} {last}".Trim();
            author = string.IsNullOrWhiteSpace(composed) ? null : composed;
        }

        return new DocumentMetadata(
            Title: string.IsNullOrWhiteSpace(title) ? null : title,
            Author: author,
            Subject: null,
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>());
    }

    public PageSize GetPageSize(int pageIndex)
    {
        EnsureValidPage(pageIndex);
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
                return new Fb2PageRender(result.WidthPx, result.HeightPx, result.Stride, result.Bgra32, GetPageSize(pageIndex));
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

        string text = _chapterTexts[chapter];
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, []));
        }

        // Один большой run на всю «страницу» — bounding box покрывает viewport.
        var run = new TextRun(text, 0, 0, DefaultPagePxWidth, DefaultPagePxHeight);
        return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, [run]));
    }

    public IDocumentEditor? GetEditor() => null;
    public IFormController? GetForms() => null;
    public ISignatureController? GetSignatures() => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Internal — экспортируем для unit-тестов без I/O.</summary>
    internal static string CollapseWhitespaceForTest(string input) => CollapseWhitespace(input);

    private void EnsureValidPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
    }

    /// <summary>One flattened FB2 chapter: its HTML fragment (for the renderer) and its plain text
    /// (for the text layer / search index).</summary>
    /// <param name="Html">The chapter's HTML fragment produced by <see cref="Fb2HtmlConverter"/>.</param>
    /// <param name="Text">The chapter's collapsed plain text.</param>
    private sealed record Fb2Chapter(string Html, string Text);
}

/// <summary>Render-обёртка над BGRA32-bitmap'ом из <see cref="IHtmlRenderer"/>. Owner = caller;
/// Dispose — no-op (no native handle).</summary>
internal sealed class Fb2PageRender(int widthPx, int heightPx, int stride, ReadOnlyMemory<byte> bgra32, PageSize pageSize) : IPageRender
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
