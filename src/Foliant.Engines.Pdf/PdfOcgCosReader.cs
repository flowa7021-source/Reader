using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Чтение OCG (Optional Content Groups) из cos-структуры PDF: навигируется по
/// <c>Catalog → /OCProperties → /OCGs</c>, парсит имена и default visibility
/// (PDF spec §8.11). Phase 2 MVP (Q-F8): только flat-список верхнеуровневых
/// слоёв; иерархия (<c>/Order</c>), OCMD, /Usage и сложные правила (<c>/VE</c>)
/// не обрабатываются и оставлены на Phase 2+/3.
///
/// Возвращает не только <see cref="PdfLayer"/> для UI, но и indirect references
/// каждого OCG-объекта — нужно writer'у (<see cref="PdfOcgCosWriter"/>), чтобы
/// собрать обновлённые <c>/ON</c>/<c>/OFF</c> массивы.
/// </summary>
internal static class PdfOcgCosReader
{
    private static readonly NameToken OCPropertiesName = NameToken.Create("OCProperties");
    private static readonly NameToken OCGsName = NameToken.Create("OCGs");
    private static readonly NameToken DName = NameToken.Create("D");
    private static readonly NameToken OnName = NameToken.Create("ON");
    private static readonly NameToken OffName = NameToken.Create("OFF");
    private static readonly NameToken BaseStateName = NameToken.Create("BaseState");
    private static readonly NameToken NameKey = NameToken.Create("Name");

    /// <summary>Читает все top-level слои документа. Если в PDF нет <c>/OCProperties</c> или
    /// <c>/OCGs</c>-массив пуст — возвращает пустой результат (но с заполненными
    /// catalog/trailer-полями для writer'а).</summary>
    public static OcgSnapshot Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        long rootObj = trailer.Root.ObjectNumber;
        long trailerSize = trailer.Size;

        if (!TryResolveOCProperties(doc, catalog, out var ocPropsRef, out var ocPropsDict))
        {
            return new OcgSnapshot([], [], null, null, catalog, rootObj, trailerSize);
        }

        if (!TryGetOCGsArray(ocPropsDict, out var ocgsArray))
        {
            return new OcgSnapshot([], [], ocPropsRef, ocPropsDict, catalog, rootObj, trailerSize);
        }

        var dDict = ResolveDefaultConfig(doc, ocPropsDict);
        var (offSet, onSet, baseState) = ReadDefaultVisibilityHints(doc, dDict);

        var layers = new List<PdfLayer>(ocgsArray.Length);
        var refs = new List<IndirectReference>(ocgsArray.Length);
        for (int i = 0; i < ocgsArray.Length; i++)
        {
            if (ocgsArray.Data[i] is not IndirectReferenceToken iref)
            {
                continue;
            }

            if (doc.Structure.GetObject(iref.Data) is not ObjectToken { Data: DictionaryToken ocgDict })
            {
                continue;
            }

            string name = ReadOcgName(ocgDict);
            bool visible = ComputeDefaultVisibility(iref.Data, offSet, onSet, baseState);
            layers.Add(new PdfLayer(i, name, visible));
            refs.Add(iref.Data);
        }

        return new OcgSnapshot(layers, refs, ocPropsRef, ocPropsDict, catalog, rootObj, trailerSize);
    }

    private static bool TryResolveOCProperties(
        PdfPigDocument doc,
        DictionaryToken catalog,
        out IndirectReference? ocPropsRef,
        out DictionaryToken ocPropsDict)
    {
        ocPropsRef = null;
        ocPropsDict = null!;
        if (!catalog.TryGet(OCPropertiesName, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case DictionaryToken inline:
                ocPropsDict = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }:
                ocPropsRef = iref.Data;
                ocPropsDict = resolved;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetOCGsArray(DictionaryToken ocPropsDict, out ArrayToken ocgsArray)
    {
        if (ocPropsDict.TryGet(OCGsName, out ArrayToken? arr) && arr is not null)
        {
            ocgsArray = arr;
            return true;
        }

        ocgsArray = new ArrayToken([]);
        return false;
    }

    private static DictionaryToken? ResolveDefaultConfig(PdfPigDocument doc, DictionaryToken ocPropsDict)
    {
        if (!ocPropsDict.TryGet(DName, out IToken? raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            DictionaryToken inline => inline,
            IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }
                => resolved,
            _ => null,
        };
    }

    private static (HashSet<IndirectReference> Off, HashSet<IndirectReference>? On, string BaseState)
        ReadDefaultVisibilityHints(PdfPigDocument doc, DictionaryToken? dDict)
    {
        var off = new HashSet<IndirectReference>();
        HashSet<IndirectReference>? on = null;
        string baseState = "ON";
        if (dDict is null)
        {
            return (off, on, baseState);
        }

        if (dDict.TryGet(BaseStateName, out NameToken? bs) && bs is not null)
        {
            baseState = bs.Data;
        }

        CollectRefsInto(doc, dDict, OffName, off);
        if (dDict.TryGet(OnName, out IToken? _))
        {
            on = [];
            CollectRefsInto(doc, dDict, OnName, on);
        }

        return (off, on, baseState);
    }

    private static void CollectRefsInto(
        PdfPigDocument doc, DictionaryToken parent, NameToken key, HashSet<IndirectReference> sink)
    {
        if (!parent.TryGet(key, out IToken? raw) || raw is null)
        {
            return;
        }

        ArrayToken? arr = raw switch
        {
            ArrayToken inline => inline,
            IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: ArrayToken resolved } => resolved,
            _ => null,
        };

        if (arr is null)
        {
            return;
        }

        foreach (var item in arr.Data)
        {
            if (item is IndirectReferenceToken iref)
            {
                sink.Add(iref.Data);
            }
        }
    }

    private static string ReadOcgName(DictionaryToken ocgDict)
    {
        if (!ocgDict.TryGet(NameKey, out IToken? raw) || raw is null)
        {
            return string.Empty;
        }

        return raw switch
        {
            StringToken s => s.Data,
            HexToken h => h.Data,
            _ => string.Empty,
        };
    }

    private static bool ComputeDefaultVisibility(
        IndirectReference ocgRef,
        HashSet<IndirectReference> offSet,
        HashSet<IndirectReference>? onSet,
        string baseState)
    {
        // PDF spec §8.11.4.4. /BaseState: ON (default) — все слои видимы кроме /OFF.
        // OFF — все скрыты кроме /ON. Unchanged — нет default'а (берётся «как был»);
        // для read-API трактуем как ON (Acrobat-стандарт).
        bool visibleByBase = !string.Equals(baseState, "OFF", StringComparison.Ordinal);
        bool isInOff = offSet.Contains(ocgRef);
        bool isInOn = onSet is not null && onSet.Contains(ocgRef);

        if (visibleByBase)
        {
            return !isInOff;
        }

        return isInOn;
    }

    /// <summary>Снимок OCG-секции PDF + ключевые catalog/trailer-поля. <see cref="Layers"/> —
    /// domain-результат для UI; остальные поля нужны writer'у для инкрементального апдейта
    /// default-visibility без повторного открытия документа.</summary>
    public sealed record OcgSnapshot(
        IReadOnlyList<PdfLayer> Layers,
        IReadOnlyList<IndirectReference> OcgRefs,
        IndirectReference? OCPropertiesRef,
        DictionaryToken? OCPropertiesDict,
        DictionaryToken CatalogDict,
        long RootObjectNumber,
        long TrailerSize);
}
