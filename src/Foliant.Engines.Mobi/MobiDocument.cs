using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Foliant.Domain;
using Foliant.Rendering.Html;

namespace Foliant.Engines.Mobi;

/// <summary>
/// MOBI (Mobipocket / Kindle) document. MOBI — это PalmDB-контейнер: 78-байтный заголовок,
/// таблица записей, затем сами записи. Запись 0 содержит PalmDOC-заголовок (тип сжатия,
/// число текстовых записей) + MOBI-заголовок (text-encoding, full-name) + EXTH-метаданные.
/// Записи <c>1..textRecordCount</c> — PalmDOC-сжатый HTML текста книги.
///
/// <para><b>Real visual rendering.</b> MOBI-текстовые записи — это произвольные ~4 КБ-нарезки ОДНОГО
/// HTML-потока (они рвутся посреди тегов), поэтому per-record-главы глючили бы на каждой границе.
/// Вместо этого все записи декодируются и <b>склеиваются</b> в одну строку (полный HTML книги),
/// которая затем <b>разбивается на главы</b> по маркерам разрыва страницы MOBI
/// (<c>&lt;mbp:pagebreak&gt;</c>). Каждая глава → <see cref="HtmlChapter"/> с
/// <see cref="NullResourceResolver.Instance"/> (MOBI-картинки отложены), а пагинация, число страниц и
/// per-page-отрисовка делегируются общему <see cref="HtmlPaginator"/> (тот же путь, что у EPUB/FB2:
/// AngleSharp parse → block/inline layout с word-wrap → SixLabors paint → BGRA32; без нативных
/// зависимостей, кросс-платформенно).</para>
///
/// <para><b>Coarse-search trade-off.</b> Когда в MOBI нет маркеров разрыва страницы, вся книга — это
/// ОДНА глава, поэтому весь её plain-text-слой для поиска (<c>SearchService</c> / FTS5) находится на
/// странице 0; на последующих страницах главы <see cref="GetTextLayerAsync"/> возвращает пустой слой.
/// Точный per-page-текст для поиска — отложенный follow-up.</para>
///
/// <para>HUFF/CDIC-сжатие и DRM не поддерживаются (Phase 1).</para>
/// </summary>
internal sealed partial class MobiDocument : IDocument
{
    /// <summary>Reference «page» width in pixels (matches <see cref="HtmlPaginator.ReferenceWidthPx"/>).</summary>
    public const int DefaultPagePxWidth = HtmlPaginator.ReferenceWidthPx;

    /// <summary>Reference «page» height in pixels (matches <see cref="HtmlPaginator.ReferenceHeightPx"/>).</summary>
    public const int DefaultPagePxHeight = HtmlPaginator.ReferenceHeightPx;

    private const int PalmDbHeaderSize = 78;
    private const int RecordEntrySize = 8;

    /// <summary>Page-break marker MOBI emits between chapters, matched case-insensitively as a
    /// substring so <c>&lt;mbp:pagebreak&gt;</c>, <c>&lt;mbp:pagebreak/&gt;</c> and attribute variants
    /// all split.</summary>
    [GeneratedRegex("<mbp:pagebreak", RegexOptions.IgnoreCase)]
    private static partial Regex PageBreakMarkerRegex();

    private readonly IReadOnlyList<string> _chapterTexts;
    private readonly HtmlPaginator _paginator;

    public DocumentKind Kind => DocumentKind.Mobi;

    /// <summary>Total page count across all chapters (Σ of per-chapter slice counts), not the
    /// text-record count.</summary>
    public int PageCount => _paginator.PageCount;

    public DocumentMetadata Metadata { get; }

    private MobiDocument(IReadOnlyList<string> chapterHtml, IReadOnlyList<string> chapterTexts, DocumentMetadata metadata, IHtmlRenderer renderer)
    {
        _chapterTexts = chapterTexts;
        _paginator = new HtmlPaginator(
            renderer,
            [.. chapterHtml.Select(html => new HtmlChapter(html, NullResourceResolver.Instance))]);
        Metadata = metadata;
    }

    /// <summary>Opens a MOBI at <paramref name="path"/> and eagerly paginates it against the
    /// supplied renderer.</summary>
    /// <param name="path">Filesystem path to the <c>.mobi</c>/<c>.prc</c>/<c>.azw</c> container.</param>
    /// <param name="renderer">The shared HTML renderer used for pagination and page painting.</param>
    /// <returns>The loaded document.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is null.</exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    public static MobiDocument Open(string path, IHtmlRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MOBI file not found.", path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        return Parse(bytes, renderer);
    }

    /// <summary>Internal — парсинг из буфера без I/O (для unit-тестов).</summary>
    /// <param name="bytes">Raw PalmDB/MOBI container bytes.</param>
    /// <param name="renderer">The shared HTML renderer used for pagination and page painting.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> or <paramref name="renderer"/> is null.</exception>
    /// <exception cref="InvalidDataException">The buffer is not a parseable, DRM-free PalmDOC MOBI.</exception>
    internal static MobiDocument Parse(byte[] bytes, IHtmlRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(renderer);
        if (bytes.Length < PalmDbHeaderSize)
        {
            throw new InvalidDataException("File is too small to be a PalmDB/MOBI container.");
        }

        // PalmDB type/creator at offset 60/64: ожидаем "BOOK"/"MOBI" (не строго — DRM-free MOBI).
        // numRecords — big-endian uint16 at offset 76.
        int numRecords = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(76, 2));
        if (numRecords == 0)
        {
            throw new InvalidDataException("PalmDB record list is empty.");
        }

        // Таблица записей: numRecords × 8 байт начиная с offset 78. Берём offset каждой записи (uint32 BE).
        int[] recordOffsets = new int[numRecords];
        for (int i = 0; i < numRecords; i++)
        {
            int entryPos = PalmDbHeaderSize + i * RecordEntrySize;
            if (entryPos + 4 > bytes.Length)
            {
                throw new InvalidDataException("Truncated PalmDB record list.");
            }
            recordOffsets[i] = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entryPos, 4));
        }

        // Запись 0: PalmDOC header (16 байт) + MOBI header.
        int rec0Start = recordOffsets[0];
        int rec0End = numRecords > 1 ? recordOffsets[1] : bytes.Length;
        if (rec0Start < 0 || rec0End > bytes.Length || rec0Start + 16 > rec0End)
        {
            throw new InvalidDataException("Invalid MOBI record-0 bounds.");
        }

        ReadOnlySpan<byte> rec0 = bytes.AsSpan(rec0Start, rec0End - rec0Start);
        int compression = BinaryPrimitives.ReadUInt16BigEndian(rec0[..2]);
        int textRecordCount = BinaryPrimitives.ReadUInt16BigEndian(rec0.Slice(8, 2));

        Encoding encoding = ReadTextEncoding(rec0);
        string title = ReadFullName(rec0, bytes, rec0Start) ?? Path.GetFileNameWithoutExtension("book");

        // Текстовые записи: 1..textRecordCount. Разжать и СКЛЕИТЬ в один HTML-поток.
        var fullHtml = new StringBuilder();
        int lastTextRecord = Math.Min(textRecordCount, numRecords - 1);
        for (int i = 1; i <= lastTextRecord; i++)
        {
            int start = recordOffsets[i];
            int end = i + 1 < numRecords ? recordOffsets[i + 1] : bytes.Length;
            if (start < 0 || end > bytes.Length || start >= end)
            {
                continue;
            }

            byte[] raw;
            try
            {
                raw = PalmDocCompression.Decompress(bytes.AsSpan(start, end - start), compression);
            }
            catch (NotSupportedException)
            {
                throw new InvalidDataException("MOBI uses HUFF/CDIC compression, which is not supported in Phase 1.");
            }

            fullHtml.Append(encoding.GetString(raw));
        }

        (IReadOnlyList<string> chapterHtml, IReadOnlyList<string> chapterTexts) = SplitIntoChapters(fullHtml.ToString());

        var metadata = new DocumentMetadata(
            Title: string.IsNullOrWhiteSpace(title) ? null : title,
            Author: null,
            Subject: null,
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>());

        return new MobiDocument(chapterHtml, chapterTexts, metadata, renderer);
    }

    /// <summary>Splits the concatenated book HTML into chapters on page-break-marker occurrences
    /// (<see cref="PageBreakMarkerRegex"/>, case-insensitive). Non-empty/non-whitespace parts become
    /// chapters; if no markers are present the whole HTML is one chapter. Always yields ≥ 1 chapter (an
    /// empty chapter when there is no text at all), so <see cref="PageCount"/> is ≥ 1. Returns the
    /// per-chapter HTML plus a parallel list of its <see cref="MobiHtml.StripToText(string)"/> plain
    /// text for the search/FTS text layer.</summary>
    private static (IReadOnlyList<string> Html, IReadOnlyList<string> Texts) SplitIntoChapters(string fullHtml)
    {
        var chapterHtml = new List<string>();

        // Split on the page-break marker substring (case-insensitive). The closing ">" of the marker
        // tag is left at the head of the following part; the HTML layout treats it as inert markup.
        string[] parts = PageBreakMarkerRegex().Split(fullHtml);
        if (parts.Length <= 1)
        {
            // No markers → the whole concatenated HTML is a single chapter.
            chapterHtml.Add(fullHtml);
        }
        else
        {
            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    chapterHtml.Add(part);
                }
            }
        }

        if (chapterHtml.Count == 0)
        {
            // No text records at all (or only whitespace) → one empty chapter (PageCount ≥ 1).
            chapterHtml.Add(string.Empty);
        }

        var chapterTexts = new List<string>(chapterHtml.Count);
        foreach (string html in chapterHtml)
        {
            chapterTexts.Add(MobiHtml.StripToText(html));
        }

        return (chapterHtml, chapterTexts);
    }

    private static Encoding ReadTextEncoding(ReadOnlySpan<byte> rec0)
    {
        // MOBI header начинается в rec0 с offset 16 идентификатором "MOBI". text-encoding —
        // uint32 BE at MOBI-offset 28 → rec0-offset 16+28 = 44. 65001 = UTF-8, 1252 = Win-1252.
        if (rec0.Length < 48 || !(rec0[16] == (byte)'M' && rec0[17] == (byte)'O' && rec0[18] == (byte)'B' && rec0[19] == (byte)'I'))
        {
            return Encoding.UTF8; // нет MOBI header — best-effort UTF-8
        }

        uint enc = BinaryPrimitives.ReadUInt32BigEndian(rec0.Slice(44, 4));
        return enc switch
        {
            65001 => Encoding.UTF8,
            1252 => CodePagesEncoding(1252),
            _ => Encoding.UTF8,
        };
    }

    private static Encoding CodePagesEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (NotSupportedException)
        {
            return Encoding.Latin1; // 1252 ≈ Latin-1 для ASCII-диапазона
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
    }

    private static string? ReadFullName(ReadOnlySpan<byte> rec0, byte[] bytes, int rec0Start)
    {
        // MOBI header: fullName offset (uint32 BE at MOBI-offset 84 → rec0-offset 100),
        // fullName length (uint32 BE at MOBI-offset 88 → rec0-offset 104). Offset отсчитывается
        // от начала записи 0 в файле.
        if (rec0.Length < 108 || rec0[16] != (byte)'M')
        {
            return null;
        }

        int nameOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(rec0.Slice(100, 4));
        int nameLength = (int)BinaryPrimitives.ReadUInt32BigEndian(rec0.Slice(104, 4));
        int absStart = rec0Start + nameOffset;
        if (nameLength <= 0 || nameLength > 1024 || absStart < 0 || absStart + nameLength > bytes.Length)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes, absStart, nameLength).Trim();
    }

    public PageSize GetPageSize(int pageIndex)
    {
        EnsureValidPage(pageIndex);
        // MOBI не имеет «point» geometry; используем пиксели как pt (1:1 на 72 DPI).
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
                return new MobiPageRender(result.WidthPx, result.HeightPx, result.Stride, result.Bgra32, GetPageSize(pageIndex));
            },
            ct);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        EnsureValidPage(pageIndex);
        ct.ThrowIfCancellationRequested();

        (int chapter, int localPage) = _paginator.Map(pageIndex);

        // The chapter's text is indexed once, on its first page; later pages of the same chapter
        // return an empty layer (precise per-page text is a future PR). With no page-break markers the
        // whole book is one chapter, so all its searchable text sits on page 0 (coarse-search).
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
        // Координаты в pt (= px при 72 DPI).
        var run = new TextRun(text, 0, 0, DefaultPagePxWidth, DefaultPagePxHeight);
        return Task.FromResult<TextLayer?>(new TextLayer(pageIndex, [run]));
    }

    public IDocumentEditor? GetEditor() => null;
    public IFormController? GetForms() => null;
    public ISignatureController? GetSignatures() => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void EnsureValidPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
    }
}

/// <summary>Render-обёртка над BGRA32-bitmap'ом из <see cref="IHtmlRenderer"/>. Owner = caller;
/// Dispose — no-op (no native handle).</summary>
internal sealed class MobiPageRender(int widthPx, int heightPx, int stride, ReadOnlyMemory<byte> bgra32, PageSize pageSize) : IPageRender
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
