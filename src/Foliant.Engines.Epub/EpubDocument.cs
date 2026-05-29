using System.Text;
using System.Text.RegularExpressions;
using Foliant.Domain;
using VersOne.Epub;

namespace Foliant.Engines.Epub;

/// <summary>
/// EPUB document backed by VersOne.Epub. Each spine item (chapter HTML) becomes one «page»
/// in the <see cref="IDocument"/> abstraction.
///
/// Phase 1 deliberate simplifications:
/// <list type="bullet">
/// <item><see cref="RenderPageAsync"/> returns a **blank white bitmap** at <see cref="DefaultPagePxWidth"/>
///   × <see cref="DefaultPagePxHeight"/>. Real font / wrap / image rendering — D6b follow-up.</item>
/// <item><see cref="GetTextLayerAsync"/> strips HTML tags via regex и возвращает один большой
///   <see cref="TextRun"/> per chapter с приблизительной bounding box. Этого достаточно для
///   поиска (<c>SearchService</c>) и FTS5-индекса.</item>
/// </list>
/// Кросс-платформенный (pure managed); тестируется на Linux.
/// </summary>
internal sealed partial class EpubDocument : IDocument
{
    /// <summary>Ширина «страницы» в пикселях для render canvas и text-layer bounding boxes.</summary>
    public const int DefaultPagePxWidth = 800;

    /// <summary>Высота «страницы» в пикселях.</summary>
    public const int DefaultPagePxHeight = 1200;

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

    public DocumentKind Kind => DocumentKind.Epub;
    public int PageCount => _spine.Count;
    public DocumentMetadata Metadata { get; }

    private EpubDocument(EpubBook book)
    {
        _book = book;
        _spine = [.. book.ReadingOrder];

        Metadata = new DocumentMetadata(
            Title: book.Title,
            Author: book.Author,
            Subject: null,
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>());
    }

    public static EpubDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("EPUB file not found.", path);
        }

        EpubBook book = EpubReader.ReadBook(path);
        return new EpubDocument(book);
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

        // Phase 1: blank canvas. Aspect-fit via opts.MaxWidthPx если задан.
        int wPx = opts.MaxWidthPx is { } maxW && maxW > 0 ? Math.Min(maxW, DefaultPagePxWidth) : DefaultPagePxWidth;
        int hPx = (int)(wPx * (double)DefaultPagePxHeight / DefaultPagePxWidth);
        int stride = wPx * 4;
        byte[] buffer = new byte[stride * hPx];
        // BGRA32 white = 0xFF FF FF FF.
        Array.Fill(buffer, (byte)0xFF);

        IPageRender render = new EpubPageRender(wPx, hPx, stride, buffer, GetPageSize(pageIndex));
        return Task.FromResult(render);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        EnsureValidPage(pageIndex);
        ct.ThrowIfCancellationRequested();

        string html = _spine[pageIndex].Content ?? string.Empty;
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
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _spine.Count);
    }
}

/// <summary>Render обёртка над blank-canvas-bitmap'ом. Owner = caller; Dispose — no-op
/// (no native handle).</summary>
internal sealed class EpubPageRender(int widthPx, int heightPx, int stride, byte[] bgra32, PageSize pageSize) : IPageRender
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
