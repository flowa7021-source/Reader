using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает именованные пункты назначения из cos-структуры PDF (ISO 32000-1 §12.3.2), учитывая обе формы
/// хранения: modern name-tree <c>Catalog → /Names → /Dests</c> (с рекурсией по <c>/Kids</c>) и legacy
/// dictionary <c>Catalog → /Dests</c>. Каждое значение — destination-массив <c>[pageRef …]</c> либо
/// dict <c>&lt;&lt; /D [pageRef …] &gt;&gt;</c>; из него берётся первый элемент-pageRef, который через
/// карту «pageRef → 0-based индекс» (обход <c>Catalog → /Pages</c>, зеркально
/// <see cref="PdfOutlineCosWriter"/>'у) превращается в <see cref="PdfNamedDestination.PageIndex"/>.
/// Записи, чью страницу разрешить не удалось, пропускаются. Симметрично
/// <see cref="PdfNamedDestinationCosWriter"/>'у.
/// </summary>
internal static class PdfNamedDestinationCosReader
{
    private static readonly NameToken NamesName = NameToken.Create("Names");
    private static readonly NameToken DestsName = NameToken.Create("Dests");
    private static readonly NameToken KidsName = NameToken.Create("Kids");
    private static readonly NameToken DName = NameToken.Create("D");

    /// <summary>Читает все именованные пункты назначения документа (обе формы). Нет пунктов → пустой
    /// список. Результат отсортирован по <see cref="PdfNamedDestination.Name"/> (ordinal).</summary>
    public static IReadOnlyList<PdfNamedDestination> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        return Read(doc);
    }

    /// <summary>Перегрузка для уже открытого документа (writer переиспользует её, чтобы не открывать
    /// PDF дважды). Семантика идентична <see cref="Read(byte[])"/>.</summary>
    public static IReadOnlyList<PdfNamedDestination> Read(PdfPigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var catalog = doc.Structure.Catalog.CatalogDictionary;
        var pageIndexByRef = BuildPageIndexMap(doc, catalog);

        // По имени дедуплицируем: если пункт есть в обеих формах, modern имеет приоритет (читается
        // первым; legacy не перезаписывает уже добавленный ключ).
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        ReadModern(doc, catalog, pageIndexByRef, byName);
        ReadLegacy(doc, catalog, pageIndexByRef, byName);

        return ToSortedList(byName);
    }

    /// <summary>
    /// Читает <b>только</b> modern-форму (<c>/Names → /Dests</c>), без legacy <c>/Dests</c>. Нужен
    /// <see cref="PdfNamedDestinationCosWriter"/>'у, чтобы сохранять существующие modern-записи, не
    /// мигрируя в неё legacy-пункты. Записи с неразрешимой страницей пропускаются; сортировка по
    /// имени (ordinal).
    /// </summary>
    public static IReadOnlyList<PdfNamedDestination> ReadModern(PdfPigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var catalog = doc.Structure.Catalog.CatalogDictionary;
        var pageIndexByRef = BuildPageIndexMap(doc, catalog);
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        ReadModern(doc, catalog, pageIndexByRef, byName);
        return ToSortedList(byName);
    }

    private static List<PdfNamedDestination> ToSortedList(Dictionary<string, int> byName)
    {
        var result = new List<PdfNamedDestination>(byName.Count);
        foreach (var kv in byName)
        {
            result.Add(new PdfNamedDestination(kv.Key, kv.Value));
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static void ReadModern(
        PdfPigDocument doc,
        DictionaryToken catalog,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        Dictionary<string, int> sink)
    {
        if (!TryResolveDict(doc, catalog, NamesName, out var names) ||
            !TryResolveDict(doc, names, DestsName, out var tree))
        {
            return;
        }

        WalkTree(doc, tree, pageIndexByRef, sink);
    }

    private static void WalkTree(
        PdfPigDocument doc,
        DictionaryToken node,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        Dictionary<string, int> sink)
    {
        if (node.TryGet(NamesName, out ArrayToken? names) && names is not null)
        {
            ReadNamePairs(doc, names, pageIndexByRef, sink);
        }

        if (node.TryGet(KidsName, out ArrayToken? kids) && kids is not null)
        {
            foreach (var kid in kids.Data)
            {
                if (kid is IndirectReferenceToken kref &&
                    doc.Structure.GetObject(kref.Data) is ObjectToken { Data: DictionaryToken child })
                {
                    WalkTree(doc, child, pageIndexByRef, sink);
                }
            }
        }
    }

    private static void ReadNamePairs(
        PdfPigDocument doc,
        ArrayToken names,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        Dictionary<string, int> sink)
    {
        // /Names — чередование [ key0 value0 key1 value1 … ]: key (i) — строка (имя пункта),
        // value (i+1) — destination (массив / dict с /D, inline или indirect).
        var data = names.Data;
        for (int i = 0; i + 1 < data.Count; i += 2)
        {
            string? name = ReadString(data[i]);
            if (name is null)
            {
                continue;
            }

            if (TryResolvePageIndex(doc, data[i + 1], pageIndexByRef, out int pageIndex))
            {
                sink.TryAdd(name, pageIndex);
            }
        }
    }

    private static void ReadLegacy(
        PdfPigDocument doc,
        DictionaryToken catalog,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        Dictionary<string, int> sink)
    {
        // Legacy: Catalog → /Dests = словарь << /Name1 [pageRef /Fit] … >> (имя — ключ-Name, а не строка).
        if (!TryResolveDict(doc, catalog, DestsName, out var dests))
        {
            return;
        }

        foreach (var kv in dests.Data)
        {
            if (TryResolvePageIndex(doc, kv.Value, pageIndexByRef, out int pageIndex))
            {
                sink.TryAdd(kv.Key, pageIndex);
            }
        }
    }

    private static bool TryResolvePageIndex(
        PdfPigDocument doc,
        IToken value,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        out int pageIndex)
    {
        pageIndex = -1;
        if (!TryResolveDestinationArray(doc, value, out var dest) || dest.Data.Count == 0)
        {
            return false;
        }

        if (dest.Data[0] is not IndirectReferenceToken pageRef)
        {
            return false;
        }

        return pageIndexByRef.TryGetValue(pageRef.Data, out pageIndex);
    }

    private static bool TryResolveDestinationArray(PdfPigDocument doc, IToken value, out ArrayToken dest)
    {
        dest = null!;
        switch (Resolve(doc, value))
        {
            case ArrayToken array:
                // Прямой destination-массив [pageRef /Fit …].
                dest = array;
                return true;
            case DictionaryToken dict
                when dict.TryGet(DName, out IToken? d) && d is not null && Resolve(doc, d) is ArrayToken inner:
                // Destination-dict << /D [pageRef …] >>.
                dest = inner;
                return true;
            default:
                return false;
        }
    }

    private static Dictionary<IndirectReference, int> BuildPageIndexMap(
        PdfPigDocument doc, DictionaryToken catalog)
    {
        var pageRefs = new List<IndirectReference>();
        if (catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) && pagesRef is not null)
        {
            WalkPages(doc, pagesRef.Data, pageRefs);
        }

        var map = new Dictionary<IndirectReference, int>(pageRefs.Count);
        for (int i = 0; i < pageRefs.Count; i++)
        {
            // Первое вхождение определяет индекс (на случай дублей в дереве страниц).
            map.TryAdd(pageRefs[i], i);
        }

        return map;
    }

    private static void WalkPages(PdfPigDocument doc, IndirectReference nodeRef, List<IndirectReference> sink)
    {
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
                    WalkPages(doc, kref.Data, sink);
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

    private static string? ReadString(IToken raw) => raw switch
    {
        HexToken h => DecodeHexString(h),
        StringToken str => str.Data,
        _ => null,
    };

    private static string DecodeHexString(HexToken hex)
    {
        // UTF-16BE с BOM (как пишет PdfTextString) декодируем явно из сырых байт — не полагаемся на
        // то, что нижележащий токенайзер уже распознал BOM (паттерн как в PdfAttachmentCosReader).
        var span = hex.Memory.Span;
        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return System.Text.Encoding.BigEndianUnicode.GetString(span[2..]);
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
