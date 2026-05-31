using System.Buffers.Binary;
using System.Text;
using Foliant.Domain;

namespace Foliant.Engines.Mobi;

/// <summary>
/// MOBI (Mobipocket / Kindle) document. MOBI — это PalmDB-контейнер: 78-байтный заголовок,
/// таблица записей, затем сами записи. Запись 0 содержит PalmDOC-заголовок (тип сжатия,
/// число текстовых записей) + MOBI-заголовок (text-encoding, full-name) + EXTH-метаданные.
/// Записи <c>1..textRecordCount</c> — PalmDOC-сжатый HTML текста книги.
///
/// Phase 1 — те же упрощения, что у EPUB/FB2:
/// <list type="bullet">
/// <item><see cref="RenderPageAsync"/> — blank white bitmap.</item>
/// <item><see cref="GetTextLayerAsync"/> — HTML-stripped текст записи в один <see cref="TextRun"/>.</item>
/// <item>Каждая декодированная текстовая запись → одна «страница».</item>
/// <item>HUFF/CDIC-сжатие и DRM не поддерживаются.</item>
/// </list>
/// </summary>
internal sealed class MobiDocument : IDocument
{
    public const int DefaultPagePxWidth = 800;
    public const int DefaultPagePxHeight = 1200;

    private const int PalmDbHeaderSize = 78;
    private const int RecordEntrySize = 8;

    private readonly IReadOnlyList<string> _pageTexts;

    public DocumentKind Kind => DocumentKind.Mobi;
    public int PageCount => _pageTexts.Count;
    public DocumentMetadata Metadata { get; }

    private MobiDocument(IReadOnlyList<string> pageTexts, DocumentMetadata metadata)
    {
        _pageTexts = pageTexts;
        Metadata = metadata;
    }

    public static MobiDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MOBI file not found.", path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    /// <summary>Internal — парсинг из буфера без I/O (для unit-тестов).</summary>
    internal static MobiDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
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

        // Текстовые записи: 1..textRecordCount. Разжать, склеить, декодировать, strip HTML, paginate.
        var pages = new List<string>();
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

            string html = encoding.GetString(raw);
            string text = MobiHtml.StripToText(html);
            if (!string.IsNullOrWhiteSpace(text))
            {
                pages.Add(text);
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(string.Empty);
        }

        var metadata = new DocumentMetadata(
            Title: string.IsNullOrWhiteSpace(title) ? null : title,
            Author: null,
            Subject: null,
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>());

        return new MobiDocument(pages, metadata);
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

        IPageRender render = new MobiPageRender(wPx, hPx, stride, buffer, GetPageSize(pageIndex));
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

    private void EnsureValidPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pageTexts.Count);
    }
}

internal sealed class MobiPageRender(int widthPx, int heightPx, int stride, byte[] bgra32, PageSize pageSize) : IPageRender
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
