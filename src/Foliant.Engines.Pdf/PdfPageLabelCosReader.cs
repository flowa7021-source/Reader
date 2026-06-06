using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает <c>/PageLabels</c> number-tree из cos-структуры PDF (Catalog → /PageLabels → /Nums, с
/// рекурсией по /Kids на случай многоузлового дерева). Каждая запись <c>[ pageIndex &lt;&lt; dict &gt;&gt; ]</c>
/// парсится в <see cref="PdfPageLabelRange"/>: <c>/S</c> → стиль, <c>/P</c> → префикс, <c>/St</c> → старт.
/// Симметрично <see cref="PdfPageLabelCosWriter"/>'у.
/// </summary>
internal static class PdfPageLabelCosReader
{
    private static readonly NameToken PageLabelsName = NameToken.Create("PageLabels");
    private static readonly NameToken NumsName = NameToken.Create("Nums");
    private static readonly NameToken KidsName = NameToken.Create("Kids");
    private static readonly NameToken SName = NameToken.Create("S");
    private static readonly NameToken PName = NameToken.Create("P");
    private static readonly NameToken StName = NameToken.Create("St");

    /// <summary>Читает все диапазоны page-label. Нет <c>/PageLabels</c> → пустой список. Результат
    /// отсортирован по <see cref="PdfPageLabelRange.StartPageIndex"/>.</summary>
    public static IReadOnlyList<PdfPageLabelRange> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!TryResolveDict(doc, catalog, PageLabelsName, out var tree))
        {
            return [];
        }

        var sink = new List<PdfPageLabelRange>();
        Walk(doc, tree, sink);
        sink.Sort(static (a, b) => a.StartPageIndex.CompareTo(b.StartPageIndex));
        return sink;
    }

    private static void Walk(PdfPigDocument doc, DictionaryToken node, List<PdfPageLabelRange> sink)
    {
        if (node.TryGet(NumsName, out ArrayToken? nums) && nums is not null)
        {
            ReadNums(doc, nums, sink);
        }

        if (node.TryGet(KidsName, out ArrayToken? kids) && kids is not null)
        {
            foreach (var kid in kids.Data)
            {
                if (kid is IndirectReferenceToken kref &&
                    doc.Structure.GetObject(kref.Data) is ObjectToken { Data: DictionaryToken child })
                {
                    Walk(doc, child, sink);
                }
            }
        }
    }

    private static void ReadNums(PdfPigDocument doc, ArrayToken nums, List<PdfPageLabelRange> sink)
    {
        // /Nums — чередование [ key0 value0 key1 value1 … ]: key — NumericToken (0-based индекс),
        // value — словарь (inline или indirect).
        var data = nums.Data;
        for (int i = 0; i + 1 < data.Count; i += 2)
        {
            if (data[i] is not NumericToken key)
            {
                continue;
            }

            int startPageIndex = key.Int;
            if (startPageIndex < 0)
            {
                continue;
            }

            if (!TryResolveValueDict(doc, data[i + 1], out var dict))
            {
                continue;
            }

            sink.Add(ParseRange(startPageIndex, dict));
        }
    }

    private static PdfPageLabelRange ParseRange(int startPageIndex, DictionaryToken dict)
    {
        PdfPageLabelStyle style = PdfPageLabelStyle.None;
        if (dict.TryGet(SName, out NameToken? s) && s is not null)
        {
            style = MapStyle(s.Data);
        }

        string? prefix = ReadPrefix(dict);

        int start = 1;
        if (dict.TryGet(StName, out NumericToken? st) && st is not null && st.Int >= 1)
        {
            start = st.Int;
        }

        return PdfPageLabelRange.Create(startPageIndex, style, prefix, start);
    }

    private static PdfPageLabelStyle MapStyle(string s) => s switch
    {
        "D" => PdfPageLabelStyle.Arabic,
        "R" => PdfPageLabelStyle.UpperRoman,
        "r" => PdfPageLabelStyle.LowerRoman,
        "A" => PdfPageLabelStyle.UpperLetters,
        "a" => PdfPageLabelStyle.LowerLetters,
        _ => PdfPageLabelStyle.Arabic, // /S присутствует, но неизвестен → есть числовая часть
    };

    private static string? ReadPrefix(DictionaryToken dict)
    {
        if (!dict.TryGet(PName, out IToken? raw) || raw is null)
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
        // то, что нижележащий токенайзер уже распознал BOM.
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

        return TryAsDict(doc, raw, out dict);
    }

    private static bool TryResolveValueDict(PdfPigDocument doc, IToken raw, out DictionaryToken dict) =>
        TryAsDict(doc, raw, out dict);

    private static bool TryAsDict(PdfPigDocument doc, IToken raw, out DictionaryToken dict)
    {
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
                dict = null!;
                return false;
        }
    }
}
