using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает link-аннотации из cos-структуры PDF (ISO 32000-1 §12.5.6.5, §12.6.4.2): строит карту
/// «pageRef → 0-based индекс» обходом <c>Catalog → /Pages → /Kids</c> (зеркально
/// <see cref="PdfNamedDestinationCosReader"/>'у, с защитой от вырожденного / циклического дерева через
/// лимит глубины), затем для каждой страницы в порядке индексов разворачивает её массив <c>/Annots</c>
/// и по каждой аннотации с <c>/Subtype /Link</c> разрешает цель: action <c>/A</c> (<c>/S /URI</c> → URI
/// из <c>/URI</c>; <c>/S /GoTo</c> → <c>/D</c>) либо прямой <c>/Dest</c>. Destination-массив
/// <c>[pageRef …]</c> → <see cref="PdfLinkAnnotation.TargetPageIndex"/> по карте; именованный пункт
/// (name/string-dest) в MVP не разрешается (цель = <see langword="null"/>). На каждую link-аннотацию
/// создаётся <see cref="PdfLinkAnnotation"/> — даже если обе цели не определены. Только чтение.
/// </summary>
internal static class PdfLinkCosReader
{
    /// <summary>Читает все link-аннотации документа. Нет ссылок → пустой список. Результат упорядочен по
    /// <see cref="PdfLinkAnnotation.PageIndex"/>, в пределах страницы — по порядку в <c>/Annots</c>.</summary>
    public static IReadOnlyList<PdfLinkAnnotation> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        return Read(doc);
    }

    /// <summary>Перегрузка для уже открытого документа (на случай переиспользования без повторного
    /// открытия PDF). Семантика идентична <see cref="Read(byte[])"/>.</summary>
    public static IReadOnlyList<PdfLinkAnnotation> Read(PdfPigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var catalog = doc.Structure.Catalog.CatalogDictionary;
        var orderedPageRefs = BuildOrderedPageRefs(doc, catalog);
        var pageIndexByRef = BuildPageIndexMap(orderedPageRefs);

        // Страницы обходим по возрастанию индекса; внутри страницы — в порядке /Annots. Так результат
        // уже упорядочен (page-major, затем discovery-order) без отдельной сортировки.
        var sink = new List<PdfLinkAnnotation>();
        for (int pageIndex = 0; pageIndex < orderedPageRefs.Count; pageIndex++)
        {
            if (doc.Structure.GetObject(orderedPageRefs[pageIndex]) is not ObjectToken { Data: DictionaryToken page })
            {
                continue;
            }

            CollectPageLinks(doc, page, pageIndex, pageIndexByRef, sink);
        }

        return sink;
    }

    private static void CollectPageLinks(
        PdfPigDocument doc,
        DictionaryToken page,
        int pageIndex,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        List<PdfLinkAnnotation> sink)
    {
        if (!TryResolveArray(doc, page, NameToken.Annots, out var annots))
        {
            return;
        }

        foreach (var entry in annots.Data)
        {
            if (Resolve(doc, entry) is not DictionaryToken annot)
            {
                continue;
            }

            if (!IsLink(annot))
            {
                continue;
            }

            ResolveTarget(doc, annot, pageIndexByRef, out string? uri, out int? targetPageIndex);
            sink.Add(new PdfLinkAnnotation(pageIndex, uri, targetPageIndex));
        }
    }

    private static bool IsLink(DictionaryToken annot) =>
        annot.TryGet(NameToken.Subtype, out NameToken? subtype) && subtype is { } s &&
        string.Equals(s.Data, NameToken.Link.Data, StringComparison.Ordinal);

    private static void ResolveTarget(
        PdfPigDocument doc,
        DictionaryToken annot,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        out string? uri,
        out int? targetPageIndex)
    {
        uri = null;
        targetPageIndex = null;

        // Приоритет — action /A (если присутствует), иначе прямой /Dest на аннотации.
        if (TryResolveDict(doc, annot, NameToken.A, out var action))
        {
            ResolveAction(doc, action, pageIndexByRef, out uri, out targetPageIndex);
            return;
        }

        if (annot.TryGet(NameToken.Dest, out IToken? dest) && dest is not null &&
            TryResolveDestPageIndex(doc, dest, pageIndexByRef, out int direct))
        {
            targetPageIndex = direct;
        }
    }

    private static void ResolveAction(
        PdfPigDocument doc,
        DictionaryToken action,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        out string? uri,
        out int? targetPageIndex)
    {
        uri = null;
        targetPageIndex = null;

        if (!action.TryGet(NameToken.S, out NameToken? subtype) || subtype is null)
        {
            return;
        }

        if (string.Equals(subtype.Data, NameToken.Uri.Data, StringComparison.Ordinal))
        {
            // URI-action: << /S /URI /URI (https://…) >>.
            uri = ReadString(action, NameToken.Uri);
            return;
        }

        if (string.Equals(subtype.Data, NameToken.GoTo.Data, StringComparison.Ordinal) &&
            action.TryGet(NameToken.D, out IToken? d) && d is not null &&
            TryResolveDestPageIndex(doc, d, pageIndexByRef, out int target))
        {
            // GoTo-action: << /S /GoTo /D dest >>.
            targetPageIndex = target;
        }
    }

    private static bool TryResolveDestPageIndex(
        PdfPigDocument doc,
        IToken dest,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        out int pageIndex)
    {
        pageIndex = -1;

        // Dest = массив [pageRef /Fit …] — берём первый элемент-pageRef. Name/string-dest (именованный
        // пункт) в MVP не разрешаем: цель остаётся неопределённой.
        if (Resolve(doc, dest) is not ArrayToken { Data.Count: > 0 } array)
        {
            return false;
        }

        if (array.Data[0] is not IndirectReferenceToken pageRef)
        {
            return false;
        }

        return pageIndexByRef.TryGetValue(pageRef.Data, out pageIndex);
    }

    private static List<IndirectReference> BuildOrderedPageRefs(PdfPigDocument doc, DictionaryToken catalog)
    {
        var pageRefs = new List<IndirectReference>();
        if (catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) && pagesRef is not null)
        {
            WalkPages(doc, pagesRef.Data, pageRefs, depth: 0);
        }

        return pageRefs;
    }

    private static Dictionary<IndirectReference, int> BuildPageIndexMap(List<IndirectReference> orderedPageRefs)
    {
        var map = new Dictionary<IndirectReference, int>(orderedPageRefs.Count);
        for (int i = 0; i < orderedPageRefs.Count; i++)
        {
            // Первое вхождение определяет индекс (на случай дублей в дереве страниц).
            map.TryAdd(orderedPageRefs[i], i);
        }

        return map;
    }

    private static void WalkPages(
        PdfPigDocument doc, IndirectReference nodeRef, List<IndirectReference> sink, int depth)
    {
        // Страховка от вырожденного / циклического /Kids (malformed PDF): дальше заданной глубины не идём (см. PdfCosLimits).
        if (depth > PdfCosLimits.MaxTreeDepth)
        {
            return;
        }

        if (doc.Structure.GetObject(nodeRef) is not ObjectToken { Data: DictionaryToken node })
        {
            return;
        }

        if (node.TryGet(NameToken.Type, out NameToken? type) && type is { } t &&
            string.Equals(t.Data, NameToken.Page.Data, StringComparison.Ordinal))
        {
            sink.Add(nodeRef);
            return;
        }

        if (node.TryGet(NameToken.Kids, out ArrayToken? kids) && kids is not null)
        {
            foreach (var kid in kids.Data)
            {
                if (kid is IndirectReferenceToken kref)
                {
                    WalkPages(doc, kref.Data, sink, depth + 1);
                }
            }
        }
    }

    private static IToken Resolve(PdfPigDocument doc, IToken token)
    {
        if (token is IndirectReferenceToken iref &&
            doc.Structure.GetObject(iref.Data) is ObjectToken { Data: IToken resolved })
        {
            return resolved;
        }

        return token;
    }

    private static string? ReadString(DictionaryToken dict, NameToken key)
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
        // UTF-16BE с BOM декодируем явно из сырых байт — не полагаемся на то, что нижележащий токенайзер
        // уже распознал BOM (паттерн как в PdfAttachmentCosReader / PdfNamedDestinationCosReader).
        var span = hex.Memory.Span;
        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return System.Text.Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        return hex.Data;
    }

    private static bool TryResolveArray(
        PdfPigDocument doc, DictionaryToken parent, NameToken key, out ArrayToken array)
    {
        array = null!;
        if (!parent.TryGet(key, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case ArrayToken inline:
                array = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: ArrayToken resolved }:
                array = resolved;
                return true;
            default:
                return false;
        }
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
