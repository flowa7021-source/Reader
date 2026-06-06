using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает встроенные файл-вложения из cos-структуры PDF (Catalog → /Names → /EmbeddedFiles, с
/// рекурсией по /Kids на случай многоузлового name-tree). Каждая пара <c>(key, filespecRef)</c> из
/// массива <c>/Names</c> разворачивается в <see cref="PdfAttachmentEntry"/>: из file-spec словаря
/// читаются <c>/F</c> (или <c>/UF</c>) и <c>/Desc</c>, по <c>/EF /F</c> находится embedded-file поток.
/// Симметрично <see cref="PdfAttachmentCosWriter"/>'у.
/// </summary>
internal static class PdfAttachmentCosReader
{
    private static readonly NameToken NamesName = NameToken.Create("Names");
    private static readonly NameToken EmbeddedFilesName = NameToken.Create("EmbeddedFiles");
    private static readonly NameToken KidsName = NameToken.Create("Kids");
    private static readonly NameToken FName = NameToken.Create("F");
    private static readonly NameToken UfName = NameToken.Create("UF");
    private static readonly NameToken EfName = NameToken.Create("EF");
    private static readonly NameToken DescName = NameToken.Create("Desc");
    private static readonly NameToken FilterName = NameToken.Create("Filter");
    private static readonly NameToken FlateDecodeName = NameToken.Create("FlateDecode");

    /// <summary>Читает все вложения документа. Нет <c>/EmbeddedFiles</c> → пустой список. Результат
    /// отсортирован по <see cref="PdfAttachmentEntry.Name"/> (ordinal).</summary>
    public static IReadOnlyList<PdfAttachmentEntry> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        return Read(doc);
    }

    /// <summary>Перегрузка для уже открытого документа (writer переиспользует её, чтобы не открывать
    /// PDF дважды). Семантика идентична <see cref="Read(byte[])"/>.</summary>
    public static IReadOnlyList<PdfAttachmentEntry> Read(PdfPigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!TryResolveDict(doc, catalog, NamesName, out var names) ||
            !TryResolveDict(doc, names, EmbeddedFilesName, out var tree))
        {
            return [];
        }

        var sink = new List<PdfAttachmentEntry>();
        Walk(doc, tree, sink, depth: 0);
        sink.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return sink;
    }

    /// <summary>
    /// Достаёт декодированные байты embedded-file потока по ссылке <paramref name="streamRef"/>:
    /// нет <c>/Filter</c> → сырые байты; <c>/FlateDecode</c> (имя или одноэлементный массив) →
    /// инфляция через zlib (PDF FlateDecode == zlib). Любой другой / составной фильтр →
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    public static byte[] ExtractBytes(PdfPigDocument doc, IndirectReference streamRef)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (doc.Structure.GetObject(streamRef) is not ObjectToken { Data: StreamToken stream })
        {
            throw new NotSupportedException(
                "Embedded file object is missing or is not a stream; cannot extract attachment bytes.");
        }

        ReadOnlyMemory<byte> raw = stream.Data;
        return DecodeStream(stream.StreamDictionary, raw);
    }

    private static byte[] DecodeStream(DictionaryToken streamDict, ReadOnlyMemory<byte> raw)
    {
        if (!TryGetSingleFilter(streamDict, out string? filter))
        {
            // Нет /Filter — байты потока хранятся как есть.
            return raw.ToArray();
        }

        if (string.Equals(filter, FlateDecodeName.Data, StringComparison.Ordinal))
        {
            return Inflate(raw);
        }

        throw new NotSupportedException(
            $"Embedded file uses unsupported stream filter '{filter}'; only FlateDecode (or none) is supported.");
    }

    private static bool TryGetSingleFilter(DictionaryToken streamDict, out string? filter)
    {
        filter = null;
        if (!streamDict.TryGet(FilterName, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case NameToken name:
                filter = name.Data;
                return true;
            case ArrayToken { Length: 1 } arr when arr.Data[0] is NameToken only:
                filter = only.Data;
                return true;
            case ArrayToken { Length: 0 }:
                return false; // пустой массив фильтров == без фильтра
            default:
                throw new NotSupportedException(
                    "Embedded file uses a multi-filter chain; only a single FlateDecode (or none) is supported.");
        }
    }

    private static byte[] Inflate(ReadOnlyMemory<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static void Walk(PdfPigDocument doc, DictionaryToken node, List<PdfAttachmentEntry> sink, int depth)
    {
        // Depth-guard against malformed/cyclic /Kids (StackOverflowException is uncatchable; see PdfCosLimits).
        if (depth > PdfCosLimits.MaxTreeDepth)
        {
            return;
        }

        if (node.TryGet(NamesName, out ArrayToken? names) && names is not null)
        {
            ReadNames(doc, names, sink);
        }

        if (node.TryGet(KidsName, out ArrayToken? kids) && kids is not null)
        {
            foreach (var kid in kids.Data)
            {
                if (kid is IndirectReferenceToken kref &&
                    doc.Structure.GetObject(kref.Data) is ObjectToken { Data: DictionaryToken child })
                {
                    Walk(doc, child, sink, depth + 1);
                }
            }
        }
    }

    private static void ReadNames(PdfPigDocument doc, ArrayToken names, List<PdfAttachmentEntry> sink)
    {
        // /Names — чередование [ key0 value0 key1 value1 … ]: key (i) — строка (имя файла),
        // value (i+1) — ссылка на file-specification словарь. Имя из ключа нам не нужно для маппинга
        // (берём /F из самого filespec'а), берём только value.
        var data = names.Data;
        for (int i = 0; i + 1 < data.Count; i += 2)
        {
            if (data[i + 1] is not IndirectReferenceToken filespecRef)
            {
                // Без indirect-ссылки на словарь запись пропускаем.
                continue;
            }

            if (doc.Structure.GetObject(filespecRef.Data) is not ObjectToken { Data: DictionaryToken filespec })
            {
                continue;
            }

            if (TryParseEntry(doc, filespecRef.Data, filespec, out var entry))
            {
                sink.Add(entry);
            }
        }
    }

    private static bool TryParseEntry(
        PdfPigDocument doc, IndirectReference filespecRef, DictionaryToken filespec, out PdfAttachmentEntry entry)
    {
        entry = null!;
        if (!TryResolveEmbeddedStreamRef(doc, filespec, out var streamRef))
        {
            return false;
        }

        string name = ReadName(filespec);
        string? description = ReadText(filespec, DescName);
        long size = ResolveDecodedLength(doc, streamRef);
        entry = new PdfAttachmentEntry(name, description, size, filespecRef, streamRef);
        return true;
    }

    private static bool TryResolveEmbeddedStreamRef(
        PdfPigDocument doc, DictionaryToken filespec, out IndirectReference streamRef)
    {
        streamRef = default;
        if (!TryResolveDict(doc, filespec, EfName, out var ef))
        {
            return false;
        }

        // /EF может ссылаться на поток через /F (а также /UF / /DOS / …); берём /F, иначе /UF.
        if (TryGetRef(ef, FName, out streamRef) || TryGetRef(ef, UfName, out streamRef))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetRef(DictionaryToken dict, NameToken key, out IndirectReference reference)
    {
        if (dict.TryGet(key, out IndirectReferenceToken? iref) && iref is not null)
        {
            reference = iref.Data;
            return true;
        }

        reference = default;
        return false;
    }

    private static long ResolveDecodedLength(PdfPigDocument doc, IndirectReference streamRef)
    {
        // Длина после декодирования: если поток сжат, читаем фактический размер; для несжатого это
        // длина сырых байт. Делаем это здесь, чтобы Size в domain-record'е отражал «настоящий» файл.
        if (doc.Structure.GetObject(streamRef) is not ObjectToken { Data: StreamToken stream })
        {
            return 0;
        }

        return DecodeStream(stream.StreamDictionary, stream.Data).LongLength;
    }

    private static string ReadName(DictionaryToken filespec) =>
        ReadText(filespec, FName) ?? ReadText(filespec, UfName) ?? string.Empty;

    private static string? ReadText(DictionaryToken dict, NameToken key)
    {
        if (!dict.TryGet(key, out IToken? raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            HexToken h => DecodeHexString(h),
            StringToken str => str.Data,
            _ => null,
        };
    }

    private static string DecodeHexString(HexToken hex)
    {
        // UTF-16BE с BOM (как пишет PdfTextString) декодируем явно из сырых байт — не полагаемся на
        // то, что нижележащий токенайзер уже распознал BOM (паттерн как в PdfPageLabelCosReader).
        var span = hex.Memory.Span;
        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        return hex.Data;
    }

    private static bool TryResolveDict(
        PdfPigDocument doc, DictionaryToken parent, NameToken key, out DictionaryToken dict)
    {
        dict = null!;
        if (!parent.TryGet(key, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case DictionaryToken inline:
                dict = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }:
                dict = resolved;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Внутренняя запись одного вложения, собранная <see cref="PdfAttachmentCosReader"/>'ом: помимо
/// публичных полей (<see cref="Name"/>, <see cref="Description"/>, <see cref="Size"/>) несёт
/// indirect-ссылки на file-spec словарь и embedded-file поток — они нужны
/// <see cref="PdfAttachmentCosWriter"/>'у, чтобы сохранять прочие вложения, и сервису, чтобы извлечь
/// байты по <see cref="StreamRef"/>.
/// </summary>
/// <param name="Name">Имя файла вложения (<c>/F</c> / <c>/UF</c>).</param>
/// <param name="Description">Описание (<c>/Desc</c>) или <see langword="null"/>.</param>
/// <param name="Size">Размер вложения в байтах после декодирования.</param>
/// <param name="FilespecRef">Ссылка на file-specification словарь.</param>
/// <param name="StreamRef">Ссылка на embedded-file поток.</param>
internal sealed record PdfAttachmentEntry(
    string Name,
    string? Description,
    long Size,
    IndirectReference FilespecRef,
    IndirectReference StreamRef);
