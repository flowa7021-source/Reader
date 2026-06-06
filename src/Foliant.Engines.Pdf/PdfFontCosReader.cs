using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает шрифты, используемые в PDF, из cos-структуры (ISO 32000-1 §9.5–§9.6): обходит дерево
/// страниц (Catalog → <c>/Pages → /Kids</c>, как <see cref="PdfOutlineCosWriter"/>'s <c>WalkPages</c>),
/// у каждой страницы резолвит <c>/Resources → /Font</c> и по каждому словарю шрифта собирает
/// <see cref="PdfFontInfo"/>: <c>/BaseFont</c> → <see cref="PdfFontInfo.Name"/>, <c>/Subtype</c> (без
/// слэша) → <see cref="PdfFontInfo.Subtype"/>, признак встраивания — наличие <c>/FontFile*</c> в
/// <c>/FontDescriptor</c> (для <c>/Type0</c> дескриптор берётся из <c>/DescendantFonts[0]</c>).
/// Один и тот же шрифт на нескольких страницах схлопывается (dedup по значению record'а).
/// </summary>
internal static class PdfFontCosReader
{
    /// <summary>Читает все шрифты документа. Нет страниц / шрифтов → пустой список. Результат
    /// отсортирован по <see cref="PdfFontInfo.Name"/> (ordinal), дубликаты убраны.</summary>
    public static IReadOnlyList<PdfFontInfo> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;

        var unique = new HashSet<PdfFontInfo>();
        if (catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) && pagesRef is not null)
        {
            WalkPages(doc, pagesRef.Data, unique);
        }

        var result = new List<PdfFontInfo>(unique);
        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static void WalkPages(PdfPigDocument doc, IndirectReference nodeRef, HashSet<PdfFontInfo> sink)
    {
        // Идентично PdfOutlineCosWriter.WalkPages: лист (/Type /Page) обрабатываем, узел (/Pages)
        // рекурсивно раскрываем по /Kids. Inheritable /Resources (ISO 32000-1 §7.7.3.4) опускаем:
        // в Acrobat-выводе /Resources почти всегда лежит на самой странице.
        if (doc.Structure.GetObject(nodeRef) is not ObjectToken { Data: DictionaryToken node })
        {
            return;
        }

        if (node.TryGet(NameToken.Type, out NameToken? type) && type is { } t &&
            string.Equals(t.Data, NameToken.Page.Data, StringComparison.Ordinal))
        {
            CollectPageFonts(doc, node, sink);
            return;
        }

        if (node.TryGet(NameToken.Kids, out ArrayToken? kids) && kids is not null)
        {
            foreach (var kid in kids.Data)
            {
                if (kid is IndirectReferenceToken kref)
                {
                    WalkPages(doc, kref.Data, sink);
                }
            }
        }
    }

    private static void CollectPageFonts(PdfPigDocument doc, DictionaryToken page, HashSet<PdfFontInfo> sink)
    {
        if (!TryResolveDict(doc, page, NameToken.Resources, out var resources) ||
            !TryResolveDict(doc, resources, NameToken.Font, out var fonts))
        {
            return;
        }

        // /Font — словарь resource-name → font dict (inline или indirect).
        foreach (var key in fonts.Data.Keys)
        {
            if (TryResolveDict(doc, fonts, NameToken.Create(key), out var fontDict))
            {
                sink.Add(ReadFont(doc, fontDict));
            }
        }
    }

    private static PdfFontInfo ReadFont(PdfPigDocument doc, DictionaryToken fontDict)
    {
        string name = ReadName(fontDict, NameToken.BaseFont);
        string subtype = ReadName(fontDict, NameToken.Subtype);
        bool embedded = IsEmbedded(doc, fontDict, subtype);
        return new PdfFontInfo(name, subtype, embedded);
    }

    private static bool IsEmbedded(PdfPigDocument doc, DictionaryToken fontDict, string subtype)
    {
        // Composite-шрифт (/Type0): сам словарь дескриптора не несёт, глифы — у потомка
        // /DescendantFonts[0] (ISO 32000-1 §9.7.4). Для остальных дескриптор лежит на самом шрифте.
        DictionaryToken descriptorOwner = fontDict;
        if (string.Equals(subtype, NameToken.Type0.Data, StringComparison.Ordinal) &&
            TryResolveDescendantFont(doc, fontDict, out var descendant))
        {
            descriptorOwner = descendant;
        }

        if (!TryResolveDict(doc, descriptorOwner, NameToken.FontDescriptor, out var descriptor))
        {
            return false;
        }

        return descriptor.ContainsKey(NameToken.FontFile) ||
            descriptor.ContainsKey(NameToken.FontFile2) ||
            descriptor.ContainsKey(NameToken.FontFile3);
    }

    private static bool TryResolveDescendantFont(
        PdfPigDocument doc, DictionaryToken fontDict, out DictionaryToken descendant)
    {
        descendant = null!;
        if (!fontDict.TryGet(NameToken.DescendantFonts, out IToken? raw) || raw is null)
        {
            return false;
        }

        // /DescendantFonts — массив ровно из одного элемента (inline или indirect), либо ссылка на
        // такой массив. Разворачиваем оба варианта и берём первый элемент.
        if (raw is IndirectReferenceToken arrRef &&
            doc.Structure.GetObject(arrRef.Data) is ObjectToken { Data: ArrayToken resolvedArr })
        {
            raw = resolvedArr;
        }

        if (raw is not ArrayToken { Length: > 0 } array)
        {
            return false;
        }

        IToken first = array.Data[0];
        switch (first)
        {
            case DictionaryToken inline:
                descendant = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }:
                descendant = resolved;
                return true;
            default:
                return false;
        }
    }

    private static string ReadName(DictionaryToken dict, NameToken key) =>
        dict.TryGet(key, out NameToken? value) && value is not null ? value.Data : string.Empty;

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
