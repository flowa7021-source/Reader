using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Engines.Fb2;

/// <summary>
/// FB2 (FictionBook 2.0) document. Каждая <c>&lt;section&gt;</c> внутри
/// <c>&lt;FictionBook&gt;/&lt;body&gt;</c> становится одной «страницей». Сложенные секции
/// (nested) flatten'ятся в плоский список для page-индекса. Если body содержит только
/// абзацы без секций — один paragraph-блок = одна страница (типичный fallback для коротких
/// книг).
///
/// Phase 1 — simplifications те же, что у EPUB:
/// <list type="bullet">
/// <item><see cref="RenderPageAsync"/> — blank white bitmap. Real text rendering — D8b.</item>
/// <item><see cref="GetTextLayerAsync"/> — конкатенация <c>&lt;p&gt;</c>/<c>&lt;title&gt;</c>/
///   <c>&lt;subtitle&gt;</c> текста секции в один <see cref="TextRun"/> для поиска и FTS5.</item>
/// </list>
/// </summary>
internal sealed class Fb2Document : IDocument
{
    public const int DefaultPagePxWidth = 800;
    public const int DefaultPagePxHeight = 1200;

    /// <summary>FB2 namespace URI. Все элементы FB2-документа в этом namespace'е.</summary>
    private static readonly XNamespace Fb2Ns = "http://www.gribuser.ru/xml/fictionbook/2.0";

    private readonly IReadOnlyList<string> _pageTexts;

    public DocumentKind Kind => DocumentKind.Fb2;
    public int PageCount => _pageTexts.Count;
    public DocumentMetadata Metadata { get; }

    private Fb2Document(IReadOnlyList<string> pageTexts, DocumentMetadata metadata)
    {
        _pageTexts = pageTexts;
        Metadata = metadata;
    }

    public static Fb2Document Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
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

        var root = xml.Root;
        if (root is null || root.Name != Fb2Ns + "FictionBook")
        {
            throw new InvalidDataException($"Root element is not <FictionBook> in FB2 namespace: {path}");
        }

        var bodies = root.Elements(Fb2Ns + "body").ToList();
        var pages = new List<string>();
        foreach (var body in bodies)
        {
            CollectPagesFromBody(body, pages);
        }
        if (pages.Count == 0)
        {
            // Документ без body — добавляем одну пустую «страницу», чтобы Pdf-style API
            // не упал на PageCount == 0.
            pages.Add(string.Empty);
        }

        var metadata = ExtractMetadata(root);
        return new Fb2Document(pages, metadata);
    }

    private static void CollectPagesFromBody(XElement body, List<string> pages)
    {
        // Top-level body: ищем <section> детей. Если нет — body становится одной страницей
        // со всем своим текстом.
        var sections = body.Elements(Fb2Ns + "section").ToList();
        if (sections.Count == 0)
        {
            string bodyText = ExtractTextFromElement(body);
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                pages.Add(CollapseWhitespace(bodyText));
            }
            return;
        }

        foreach (var section in sections)
        {
            CollectPagesFromSection(section, pages);
        }
    }

    /// <summary>Рекурсивно flatten'ит секцию в плоский список страниц. Прямой текст
    /// (title/subtitle/p) становится одной страницей; каждая nested-секция — следующая.</summary>
    private static void CollectPagesFromSection(XElement section, List<string> pages)
    {
        string directText = ExtractDirectTextFromSection(section);
        if (!string.IsNullOrWhiteSpace(directText))
        {
            pages.Add(directText);
        }
        foreach (var nested in section.Elements(Fb2Ns + "section"))
        {
            CollectPagesFromSection(nested, pages);
        }
    }

    /// <summary>Достаёт текст из <c>&lt;title&gt;</c>, <c>&lt;subtitle&gt;</c>, <c>&lt;p&gt;</c>
    /// детей данной секции (не рекурсивно — nested sections обрабатываются отдельной итерацией).</summary>
    private static string ExtractDirectTextFromSection(XElement section)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in section.Elements())
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
        var sb = new System.Text.StringBuilder(text.Length);
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
        var titleInfo = root.Element(Fb2Ns + "description")?.Element(Fb2Ns + "title-info");
        string? title = titleInfo?.Element(Fb2Ns + "book-title")?.Value?.Trim();
        var firstAuthor = titleInfo?.Element(Fb2Ns + "author");
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

        int wPx = opts.MaxWidthPx is { } maxW && maxW > 0 ? Math.Min(maxW, DefaultPagePxWidth) : DefaultPagePxWidth;
        int hPx = (int)(wPx * (double)DefaultPagePxHeight / DefaultPagePxWidth);
        int stride = wPx * 4;
        byte[] buffer = new byte[stride * hPx];
        Array.Fill(buffer, (byte)0xFF); // white BGRA

        IPageRender render = new Fb2PageRender(wPx, hPx, stride, buffer, GetPageSize(pageIndex));
        return Task.FromResult(render);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        EnsureValidPage(pageIndex);
        ct.ThrowIfCancellationRequested();

        string text = _pageTexts[pageIndex];
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, []));
        }

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
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pageTexts.Count);
    }
}

internal sealed class Fb2PageRender(int widthPx, int heightPx, int stride, byte[] bgra32, PageSize pageSize) : IPageRender
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
