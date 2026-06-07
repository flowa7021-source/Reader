using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает <b>богатое</b> PDF /Outlines дерево из cos-структуры (ISO 32000-1 §12.3.3) — симметрично
/// <see cref="PdfOutlineCosWriter"/>'у, но в обратную сторону. Навигируется по <c>Catalog → /Outlines</c>,
/// затем обходит дерево в pre-order: <c>/First</c> (спуск, depth+1) → <c>/Next</c> (сиблинг, та же
/// глубина). Для каждого узла собирает <see cref="DocumentOutlineEntry"/>:
/// <list type="bullet">
///   <item><description><c>/Title</c> — text-string (UTF-16BE с BOM либо ASCII literal), декодируется
///   так же, как пишет <see cref="PdfTextString"/>.</description></item>
///   <item><description>PageIndex — из <c>/Dest</c> (массив / name / string) либо action <c>/A</c>
///   (<c>/S /GoTo</c> + <c>/D</c>); первый элемент массива (pageRef) → 0-based индекс по карте обхода
///   <c>/Pages → /Kids</c>. Неразрешимый узел <b>не отбрасывается</b> — попадает с PageIndex = -1.</description></item>
///   <item><description>Режим отображения — из mode-токена dest-массива: <c>/Fit</c>→FitPage,
///   <c>/FitH</c>→FitWidth, <c>/FitV</c>→FitHeight, <c>/XYZ</c>→InheritZoom, иначе FitPage.</description></item>
///   <item><description>Bold/Italic — из <c>/F</c> (бит 2 = bold, бит 1 = italic); цвет — из <c>/C [r g b]</c>;
///   open/closed — из знака <c>/Count</c> (&lt;0 — свёрнут, иначе развёрнут; листья — развёрнуты).</description></item>
/// </list>
///
/// <para>Best-effort на всех уровнях: сбой парсинга конкретного узла → узел/поддерево пропускается,
/// обход продолжается. Рекурсия по <c>/First</c> и итерация по <c>/Next</c> ограничены
/// <see cref="PdfCosLimits.MaxTreeDepth"/> + visited-set'ом — вырожденное / циклическое дерево
/// гарантированно завершается (StackOverflowException неотлавливаем, см. <see cref="PdfCosLimits"/>).</para>
/// </summary>
internal static class PdfOutlineCosReader
{
    /// <summary>Читает богатое /Outlines из байт PDF. Нет outline'а / битый PDF → пустой список.
    /// Порядок — pre-order, глубина — 0 для корневой цепочки, +1 на каждый спуск по <c>/First</c>.</summary>
    public static IReadOnlyList<DocumentOutlineEntry> Read(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var doc = PdfPigDocument.Open(source);
        return Read(doc);
    }

    /// <summary>Перегрузка для уже открытого документа (без повторного открытия PDF). Семантика
    /// идентична <see cref="Read(byte[])"/>.</summary>
    public static IReadOnlyList<DocumentOutlineEntry> Read(PdfPigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!TryResolveDict(doc, catalog, NameToken.Outlines, out var outlines))
        {
            return [];
        }

        var pageIndexByRef = BuildPageIndexMap(doc, catalog);
        var namedDests = NamedDestinations.Build(doc, catalog, pageIndexByRef);

        var result = new List<DocumentOutlineEntry>();
        // Корневая цепочка начинается с /First; visited защищает /Next-итерацию от циклов.
        if (outlines.TryGet(NameToken.First, out IndirectReferenceToken? firstRef) && firstRef is not null)
        {
            WalkSiblings(doc, firstRef.Data, depth: 0, pageIndexByRef, namedDests, result, []);
        }

        return result;
    }

    /// <summary>Обходит цепочку сиблингов (узел → его дети через рекурсию → следующий <c>/Next</c>),
    /// итеративно (а не рекурсивно по /Next), чтобы длинная плоская цепочка не упёрлась в стек.</summary>
    private static void WalkSiblings(
        PdfPigDocument doc,
        IndirectReference nodeRef,
        int depth,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        IReadOnlyDictionary<string, NamedDestination> namedDests,
        List<DocumentOutlineEntry> sink,
        HashSet<IndirectReference> visited)
    {
        // Спуск по /First ограничен глубиной (depth-guard). На той же глубине /Next защищён visited-set'ом
        // плюс жёстким лимитом итераций — оба независимо гарантируют завершение на битом дереве.
        if (depth > PdfCosLimits.MaxTreeDepth)
        {
            return;
        }

        var current = nodeRef;
        for (int guard = 0; guard <= PdfCosLimits.MaxTreeDepth * MaxSiblingFactor; guard++)
        {
            if (!visited.Add(current))
            {
                // Цикл в /Next (или ссылка на уже посещённый узел) — останавливаем цепочку.
                return;
            }

            if (!TryGetDictionary(doc, current, out var node))
            {
                // Dangling / non-dict /Next ссылка (битый PDF) — обрываем цепочку, собранное сохраняется.
                return;
            }

            AppendNode(doc, node, depth, pageIndexByRef, namedDests, sink, visited);

            if (!node.TryGet(NameToken.Next, out IndirectReferenceToken? next) || next is null)
            {
                return;
            }

            current = next.Data;
        }
    }

    /// <summary>Эмитит запись для одного узла (best-effort: исключение → узел и его поддерево
    /// пропускаются), затем спускается в детей по <c>/First</c>.</summary>
    private static void AppendNode(
        PdfPigDocument doc,
        DictionaryToken node,
        int depth,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        IReadOnlyDictionary<string, NamedDestination> namedDests,
        List<DocumentOutlineEntry> sink,
        HashSet<IndirectReference> visited)
    {
        var entry = TryReadNode(doc, node, depth, pageIndexByRef, namedDests);
        if (entry is null)
        {
            return;
        }

        sink.Add(entry);

        if (node.TryGet(NameToken.First, out IndirectReferenceToken? childRef) && childRef is not null)
        {
            WalkSiblings(doc, childRef.Data, depth + 1, pageIndexByRef, namedDests, sink, visited);
        }
    }

    private static DocumentOutlineEntry? TryReadNode(
        PdfPigDocument doc,
        DictionaryToken node,
        int depth,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        IReadOnlyDictionary<string, NamedDestination> namedDests)
    {
        try
        {
            string title = ReadTitle(node);
            var (pageIndex, mode) = ResolveDestination(doc, node, pageIndexByRef, namedDests);
            (bool isBold, bool isItalic) = ReadStyleFlags(node);
            OutlineColor? color = ReadColor(node);
            bool isOpen = ReadIsOpen(node);

            return new DocumentOutlineEntry(pageIndex, title, depth, mode, isBold, isItalic, color, isOpen);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            // Один битый узел не должен ронять весь обход — пропускаем его (и его поддерево).
            return null;
        }
    }

    private static string ReadTitle(DictionaryToken node) =>
        ReadTextString(node, NameToken.Title) ?? string.Empty;

    // ---- Destination -------------------------------------------------------------------------------

    private static (int PageIndex, OutlineDestinationMode Mode) ResolveDestination(
        PdfPigDocument doc,
        DictionaryToken node,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        IReadOnlyDictionary<string, NamedDestination> namedDests)
    {
        // Приоритет: прямой /Dest, иначе action /A с /S /GoTo и /D.
        if (node.TryGet(NameToken.Dest, out IToken? dest) && dest is not null &&
            TryResolveDest(doc, dest, pageIndexByRef, namedDests, out int destPage, out var destMode))
        {
            return (destPage, destMode);
        }

        if (TryResolveDict(doc, node, NameToken.A, out var action) && IsGoToAction(action) &&
            action.TryGet(NameToken.D, out IToken? d) && d is not null &&
            TryResolveDest(doc, d, pageIndexByRef, namedDests, out int actionPage, out var actionMode))
        {
            return (actionPage, actionMode);
        }

        // Узел без разрешимого destination остаётся видимым (PageIndex = -1, дефолтный режим).
        return (-1, OutlineDestinationMode.FitPage);
    }

    private static bool IsGoToAction(DictionaryToken action) =>
        action.TryGet(NameToken.S, out NameToken? subtype) && subtype is { } s &&
        string.Equals(s.Data, NameToken.GoTo.Data, StringComparison.Ordinal);

    /// <summary>Разрешает destination любой формы (массив / name / string) в (pageIndex, mode).
    /// Возвращает <see langword="false"/>, только если форма совсем не распознана.</summary>
    private static bool TryResolveDest(
        PdfPigDocument doc,
        IToken dest,
        IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
        IReadOnlyDictionary<string, NamedDestination> namedDests,
        out int pageIndex,
        out OutlineDestinationMode mode)
    {
        pageIndex = -1;
        mode = OutlineDestinationMode.FitPage;

        switch (Resolve(doc, dest))
        {
            case ArrayToken array:
                pageIndex = ResolvePageIndexFromArray(array, pageIndexByRef);
                mode = ReadModeFromArray(array);
                return true;
            case NameToken name when namedDests.TryGetValue(name.Data, out var byName):
                pageIndex = byName.PageIndex;
                mode = byName.Mode;
                return true;
            case StringToken str when namedDests.TryGetValue(str.Data, out var byStr):
                pageIndex = byStr.PageIndex;
                mode = byStr.Mode;
                return true;
            case HexToken hex when namedDests.TryGetValue(DecodeHexString(hex), out var byHex):
                pageIndex = byHex.PageIndex;
                mode = byHex.Mode;
                return true;
            default:
                return false;
        }
    }

    private static int ResolvePageIndexFromArray(
        ArrayToken array, IReadOnlyDictionary<IndirectReference, int> pageIndexByRef)
    {
        if (array.Data.Count > 0 && array.Data[0] is IndirectReferenceToken pageRef &&
            pageIndexByRef.TryGetValue(pageRef.Data, out int idx))
        {
            return idx;
        }

        return -1;
    }

    /// <summary>Mode-токен — второй элемент dest-массива <c>[pageRef /Fit …]</c>. Незнакомый /
    /// отсутствующий → FitPage (как и в writer'е для экзотических режимов).</summary>
    private static OutlineDestinationMode ReadModeFromArray(ArrayToken array)
    {
        if (array.Data.Count < 2 || array.Data[1] is not NameToken modeToken)
        {
            return OutlineDestinationMode.FitPage;
        }

        return modeToken.Data switch
        {
            "Fit" => OutlineDestinationMode.FitPage,
            "FitH" => OutlineDestinationMode.FitWidth,
            "FitV" => OutlineDestinationMode.FitHeight,
            "XYZ" => OutlineDestinationMode.InheritZoom,
            _ => OutlineDestinationMode.FitPage,
        };
    }

    // ---- Style / colour / open --------------------------------------------------------------------

    private static (bool IsBold, bool IsItalic) ReadStyleFlags(DictionaryToken node)
    {
        if (!node.TryGet(NameToken.F, out NumericToken? flags) || flags is null)
        {
            return (false, false);
        }

        // /F (Table 153): бит 1 (значение 1) = italic, бит 2 (значение 2) = bold.
        int value = flags.Int;
        return ((value & 2) != 0, (value & 1) != 0);
    }

    private static OutlineColor? ReadColor(DictionaryToken node)
    {
        if (!node.TryGet(NameToken.C, out ArrayToken? color) || color is null || color.Data.Count < 3)
        {
            return null;
        }

        if (color.Data[0] is NumericToken r && color.Data[1] is NumericToken g && color.Data[2] is NumericToken b)
        {
            return new OutlineColor(r.Double, g.Double, b.Double);
        }

        return null;
    }

    private static bool ReadIsOpen(DictionaryToken node)
    {
        // Знак /Count несёт open/closed (ISO 32000-1 §12.3.3): <0 — свёрнут. Отсутствует (лист) — открыт.
        if (node.TryGet(NameToken.Count, out NumericToken? count) && count is not null)
        {
            return count.Int >= 0;
        }

        return true;
    }

    // ---- Named destinations -----------------------------------------------------------------------

    /// <summary>Разрешённый именованный пункт назначения: 0-based страница + режим отображения.</summary>
    private readonly record struct NamedDestination(int PageIndex, OutlineDestinationMode Mode);

    /// <summary>Best-effort построение карты «имя → (pageIndex, mode)» из обеих форм хранения
    /// (modern <c>/Names/Dests</c> name-tree + legacy <c>/Dests</c> dict). Self-contained (а не через
    /// <see cref="PdfNamedDestinationCosReader"/>), потому что inspector'у нужен ещё и mode-токен, и
    /// сохранение PageIndex = -1 для неразрешимых страниц.</summary>
    private static class NamedDestinations
    {
        public static Dictionary<string, NamedDestination> Build(
            PdfPigDocument doc,
            DictionaryToken catalog,
            IReadOnlyDictionary<IndirectReference, int> pageIndexByRef)
        {
            var map = new Dictionary<string, NamedDestination>(StringComparer.Ordinal);
            try
            {
                ReadModern(doc, catalog, pageIndexByRef, map);
                ReadLegacy(doc, catalog, pageIndexByRef, map);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
            {
                // Битое дерево named-dest не должно ронять чтение outline'а — отдаём, что собрали.
            }

            return map;
        }

        private static void ReadModern(
            PdfPigDocument doc,
            DictionaryToken catalog,
            IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
            Dictionary<string, NamedDestination> sink)
        {
            if (TryResolveDict(doc, catalog, NameToken.Names, out var names) &&
                TryResolveDict(doc, names, NameToken.Dests, out var tree))
            {
                WalkNameTree(doc, tree, pageIndexByRef, sink, depth: 0);
            }
        }

        private static void WalkNameTree(
            PdfPigDocument doc,
            DictionaryToken node,
            IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
            Dictionary<string, NamedDestination> sink,
            int depth)
        {
            if (depth > PdfCosLimits.MaxTreeDepth)
            {
                return;
            }

            if (node.TryGet(NameToken.Names, out ArrayToken? names) && names is not null)
            {
                var data = names.Data;
                for (int i = 0; i + 1 < data.Count; i += 2)
                {
                    string? name = ReadStringToken(data[i]);
                    if (name is not null)
                    {
                        AddDest(doc, name, data[i + 1], pageIndexByRef, sink);
                    }
                }
            }

            if (node.TryGet(NameToken.Kids, out ArrayToken? kids) && kids is not null)
            {
                foreach (var kid in kids.Data)
                {
                    if (kid is IndirectReferenceToken kref &&
                        doc.Structure.GetObject(kref.Data) is ObjectToken { Data: DictionaryToken child })
                    {
                        WalkNameTree(doc, child, pageIndexByRef, sink, depth + 1);
                    }
                }
            }
        }

        private static void ReadLegacy(
            PdfPigDocument doc,
            DictionaryToken catalog,
            IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
            Dictionary<string, NamedDestination> sink)
        {
            if (!TryResolveDict(doc, catalog, NameToken.Dests, out var dests))
            {
                return;
            }

            foreach (var kv in dests.Data)
            {
                AddDest(doc, kv.Key, kv.Value, pageIndexByRef, sink);
            }
        }

        private static void AddDest(
            PdfPigDocument doc,
            string name,
            IToken value,
            IReadOnlyDictionary<IndirectReference, int> pageIndexByRef,
            Dictionary<string, NamedDestination> sink)
        {
            if (sink.ContainsKey(name))
            {
                // Modern читается первым и имеет приоритет — legacy не перезаписывает.
                return;
            }

            if (TryResolveDestArray(doc, value, out var array))
            {
                int pageIndex = ResolvePageIndexFromArray(array, pageIndexByRef);
                sink[name] = new NamedDestination(pageIndex, ReadModeFromArray(array));
            }
        }

        private static bool TryResolveDestArray(PdfPigDocument doc, IToken value, out ArrayToken array)
        {
            array = null!;
            switch (Resolve(doc, value))
            {
                case ArrayToken direct:
                    array = direct;
                    return true;
                case DictionaryToken dict
                    when dict.TryGet(NameToken.D, out IToken? d) && d is not null && Resolve(doc, d) is ArrayToken inner:
                    array = inner;
                    return true;
                default:
                    return false;
            }
        }
    }

    // ---- Page-tree map -----------------------------------------------------------------------------

    private static Dictionary<IndirectReference, int> BuildPageIndexMap(
        PdfPigDocument doc, DictionaryToken catalog)
    {
        var pageRefs = new List<IndirectReference>();
        if (catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) && pagesRef is not null)
        {
            WalkPages(doc, pagesRef.Data, pageRefs, depth: 0);
        }

        var map = new Dictionary<IndirectReference, int>(pageRefs.Count);
        for (int i = 0; i < pageRefs.Count; i++)
        {
            // Первое вхождение определяет индекс (на случай дублей в дереве страниц).
            map.TryAdd(pageRefs[i], i);
        }

        return map;
    }

    private static void WalkPages(
        PdfPigDocument doc, IndirectReference nodeRef, List<IndirectReference> sink, int depth)
    {
        // Depth-guard against malformed/cyclic /Kids (StackOverflowException is uncatchable; see PdfCosLimits).
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

    // ---- Token helpers -----------------------------------------------------------------------------

    /// <summary>Безопасно разрешает indirect ref в его dictionary. Возвращает <see langword="false"/>
    /// (а не бросает), если ссылка повисшая — <see cref="PdfPigDocument"/> бросает на неизвестный объект
    /// <see cref="InvalidOperationException"/>; в обходе сиблингов это означает «оборвать цепочку,
    /// сохранив собранное».</summary>
    private static bool TryGetDictionary(PdfPigDocument doc, IndirectReference reference, out DictionaryToken dict)
    {
        dict = null!;
        try
        {
            if (doc.Structure.GetObject(reference) is ObjectToken { Data: DictionaryToken node })
            {
                dict = node;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Повисшая ссылка в /First / /Next — best-effort: дальше по этой цепочке не идём.
        }

        return false;
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

    private static string? ReadTextString(DictionaryToken dict, NameToken key)
    {
        if (!dict.TryGet(key, out IToken? raw) || raw is null)
        {
            return null;
        }

        return ReadStringToken(raw);
    }

    private static string? ReadStringToken(IToken raw) => raw switch
    {
        HexToken h => DecodeHexString(h),
        StringToken str => str.Data,
        _ => null,
    };

    private static string DecodeHexString(HexToken hex)
    {
        // UTF-16BE с BOM (как пишет PdfTextString) декодируем явно из сырых байт — не полагаемся на
        // то, что нижележащий токенайзер уже распознал BOM (паттерн как в PdfNamedDestinationCosReader).
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

    /// <summary>Множитель лимита итераций /Next-цепочки: число сиблингов может законно сильно
    /// превышать глубину дерева, но всё равно должно быть конечным. Лимит — последняя страховка от
    /// цикла, не отлавливаемого visited-set'ом (на практике visited срабатывает первым).</summary>
    private const int MaxSiblingFactor = 4096;
}
