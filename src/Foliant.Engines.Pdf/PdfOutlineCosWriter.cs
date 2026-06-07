using System.Globalization;
using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Строит PDF /Outlines дерево из плоского списка <see cref="DocumentOutlineEntry"/> и дописывает
/// его к исходным байтам инкрементальным апдейтом (симметрично <see cref="PdfPigOutlineReader"/>'у,
/// но в обратную сторону). Mirror'ит <see cref="PdfPigAnnotationAppender"/>: читает PDF PdfPig'ом,
/// навигирует Catalog→/Pages→/Kids для IndirectReference каждой страницы, эмитит N item-объектов +
/// 1 root <c>/Outlines</c> + обновлённый catalog с <c>/Outlines N 0 R</c>, и зовёт
/// <see cref="PdfIncrementalWriter"/>. Оригинальные байты не меняются.
///
/// Дерево восстанавливается из <see cref="DocumentOutlineEntry.Depth"/> через depth-stack: глубина
/// больше текущей трактуется как ребёнок последнего узла; linkage (/First /Last /Next /Prev /Parent)
/// и /Count считаются попутно. PageIndex вне диапазона зажимается в <c>[0, pageCount-1]</c>.
/// </summary>
internal static class PdfOutlineCosWriter
{
    /// <summary>Пустой список → catalog с <c>/Outlines</c> = null (исходный outline отбрасывается),
    /// иначе — root + item-объекты. Возвращает байты нового PDF; <paramref name="source"/> не мутируется.</summary>
    public static byte[] WriteOutline(byte[] source, IReadOnlyList<DocumentOutlineEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var pageRefs = CollectPageRefs(doc);
        long nextObj = MaxObjectNumber(doc) + 1;

        IReadOnlyList<PdfIncrementalWriter.RawObject> newObjects = [];
        IndirectReference? outlineRootRef = null;
        if (entries.Count > 0)
        {
            (newObjects, outlineRootRef) = BuildOutlineObjects(entries, pageRefs, ref nextObj);
        }

        string catalogBody = PdfCatalogOutlineCosWriter.WriteCatalogWithOutline(
            doc.Structure.Catalog.CatalogDictionary, outlineRootRef);
        var updated = new[] { new PdfIncrementalWriter.RawObject(trailer.Root, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, newObjects, updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static (IReadOnlyList<PdfIncrementalWriter.RawObject> Objects, IndirectReference Root) BuildOutlineObjects(
        IReadOnlyList<DocumentOutlineEntry> entries, IReadOnlyList<IndirectReference> pageRefs, ref long nextObj)
    {
        var rootRef = new IndirectReference(nextObj++, 0);
        var itemRefs = new IndirectReference[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            itemRefs[i] = new IndirectReference(nextObj++, 0);
        }

        var links = OutlineLinks.Compute(entries, rootRef, itemRefs);
        var objects = new List<PdfIncrementalWriter.RawObject>(entries.Count + 1)
        {
            new(rootRef, WriteRoot(links)),
        };

        for (int i = 0; i < entries.Count; i++)
        {
            objects.Add(new(itemRefs[i], WriteItem(entries[i], links.Items[i], pageRefs)));
        }

        return (objects, rootRef);
    }

    private static string WriteRoot(OutlineLinkTable links)
    {
        var sb = new StringBuilder("<<\n/Type /Outlines\n");
        if (links.RootFirst is { } first && links.RootLast is { } last)
        {
            sb.Append(CultureInfo.InvariantCulture, $"/First {Ref(first)}\n/Last {Ref(last)}\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"/Count {links.RootCount}\n>>");
        return sb.ToString();
    }

    private static string WriteItem(DocumentOutlineEntry entry, OutlineItemLinks link, IReadOnlyList<IndirectReference> pageRefs)
    {
        var sb = new StringBuilder("<<\n/Title ");
        PdfTextString.Append(sb, entry.Title);
        sb.Append(CultureInfo.InvariantCulture, $"\n/Parent {Ref(link.Parent)}\n");
        AppendOptionalRef(sb, "/Prev", link.Prev);
        AppendOptionalRef(sb, "/Next", link.Next);
        if (link.First is { } f && link.Last is { } l)
        {
            // /Count sign carries the open/closed state for nodes with children (ISO 32000-1
            // §12.3.3): ≥0 = expanded, <0 = collapsed. Leaves omit /Count.
            int count = entry.IsOpen ? link.ChildCount : -link.ChildCount;
            sb.Append(CultureInfo.InvariantCulture, $"/First {Ref(f)}\n/Last {Ref(l)}\n/Count {count}\n");
        }

        // /F flag (Table 153): bit 1 (value 1) = italic, bit 2 (value 2) = bold. Omit when plain.
        int styleFlags = (entry.IsItalic ? 1 : 0) | (entry.IsBold ? 2 : 0);
        if (styleFlags != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"/F {styleFlags}\n");
        }

        // /C [r g b] title colour (components 0..1); omit for the default (black).
        if (entry.Color is { } c)
        {
            sb.Append(CultureInfo.InvariantCulture, $"/C [{Num(c.Red)} {Num(c.Green)} {Num(c.Blue)}]\n");
        }

        var pageRef = pageRefs[Math.Clamp(entry.PageIndex, 0, pageRefs.Count - 1)];
        sb.Append(CultureInfo.InvariantCulture, $"/Dest [{Ref(pageRef)} {DestTokens(entry.Destination)}]\n>>");
        return sb.ToString();
    }

    /// <summary>Explicit-destination tokens for a zoom mode (ISO 32000-1 §12.3.2.2). <c>null</c>
    /// operands mean "retain the current value", so e.g. FitWidth keeps the vertical position.</summary>
    private static string DestTokens(OutlineDestinationMode mode) => mode switch
    {
        OutlineDestinationMode.FitWidth => "/FitH null",
        OutlineDestinationMode.FitHeight => "/FitV null",
        OutlineDestinationMode.InheritZoom => "/XYZ null null null",
        _ => "/Fit",
    };

    /// <summary>Format a colour component clamped to the PDF 0..1 range, invariant culture, trimmed.</summary>
    private static string Num(double value) =>
        Math.Clamp(value, 0.0, 1.0).ToString("0.####", CultureInfo.InvariantCulture);

    private static void AppendOptionalRef(StringBuilder sb, string key, IndirectReference? reference)
    {
        if (reference is { } r)
        {
            sb.Append(key).Append(' ').Append(Ref(r)).Append('\n');
        }
    }

    private static List<IndirectReference> CollectPageRefs(PdfPigDocument doc)
    {
        var refs = new List<IndirectReference>();
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) && pagesRef is not null)
        {
            WalkPages(doc, pagesRef.Data, refs, depth: 0);
        }

        if (refs.Count == 0)
        {
            throw new InvalidDataException("PDF has no page objects; cannot anchor outline destinations.");
        }

        return refs;
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

    private static long MaxObjectNumber(PdfPigDocument doc)
    {
        long max = 0;
        foreach (var key in doc.Structure.CrossReferenceTable.ObjectOffsets.Keys)
        {
            if (key.ObjectNumber > max)
            {
                max = key.ObjectNumber;
            }
        }

        return max;
    }

    private static string Ref(IndirectReference r) =>
        string.Create(CultureInfo.InvariantCulture, $"{r.ObjectNumber} {r.Generation} R");
}
