using System.Globalization;
using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Строит <c>/PageLabels</c> number-tree из списка <see cref="PdfPageLabelRange"/> и дописывает его к
/// исходным байтам инкрементальным апдейтом (симметрично <see cref="PdfPageLabelCosReader"/>'у).
/// Эмитит один объект <c>&lt;&lt; /Nums [ … ] &gt;&gt;</c> + обновлённый catalog с <c>/PageLabels N 0 R</c>
/// и зовёт <see cref="PdfIncrementalWriter"/>. Оригинальные байты не меняются (ISO 32000-1 §7.5.6).
/// </summary>
internal static class PdfPageLabelCosWriter
{
    /// <summary>Пустой список → catalog с <c>/PageLabels</c> = null (исходная нумерация
    /// отбрасывается), иначе — number-tree объект + ссылка из catalog'а. Возвращает байты нового PDF;
    /// <paramref name="source"/> не мутируется.</summary>
    public static byte[] Write(byte[] source, IReadOnlyList<PdfPageLabelRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        long nextObj = MaxObjectNumber(doc) + 1;

        IReadOnlyList<PdfIncrementalWriter.RawObject> newObjects = [];
        IndirectReference? pageLabelsRef = null;
        if (ranges.Count > 0)
        {
            var sorted = NormalizeAndValidate(ranges);
            var rootRef = new IndirectReference(nextObj++, 0);
            pageLabelsRef = rootRef;
            newObjects = [new PdfIncrementalWriter.RawObject(rootRef, WritePageLabelsDict(sorted))];
        }

        string catalogBody = PdfCatalogPageLabelCosWriter.WriteCatalogWithPageLabels(catalog, pageLabelsRef);
        var updated = new[] { new PdfIncrementalWriter.RawObject(trailer.Root, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, newObjects, updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static List<PdfPageLabelRange> NormalizeAndValidate(IReadOnlyList<PdfPageLabelRange> ranges)
    {
        // Number-tree ключи обязаны идти по возрастанию и быть уникальными (ISO 32000-1 §7.9.7).
        var sorted = new List<PdfPageLabelRange>(ranges);
        sorted.Sort(static (a, b) => a.StartPageIndex.CompareTo(b.StartPageIndex));
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].StartPageIndex < 0)
            {
                throw new ArgumentException("Page-label StartPageIndex must be non-negative.", nameof(ranges));
            }

            if (i > 0 && sorted[i].StartPageIndex == sorted[i - 1].StartPageIndex)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Duplicate page-label StartPageIndex {sorted[i].StartPageIndex}."),
                    nameof(ranges));
            }
        }

        return sorted;
    }

    private static string WritePageLabelsDict(List<PdfPageLabelRange> sorted)
    {
        var sb = new StringBuilder("<<\n/Nums [");
        foreach (var r in sorted)
        {
            sb.Append(CultureInfo.InvariantCulture, $" {r.StartPageIndex} ");
            AppendRangeDict(sb, r);
        }

        sb.Append(" ]\n>>");
        return sb.ToString();
    }

    private static void AppendRangeDict(StringBuilder sb, PdfPageLabelRange r)
    {
        sb.Append("<<");
        string? styleName = StyleName(r.Style);
        if (styleName is not null)
        {
            sb.Append(" /S /").Append(styleName);
        }

        if (!string.IsNullOrEmpty(r.Prefix))
        {
            sb.Append(" /P ");
            PdfTextString.Append(sb, r.Prefix);
        }

        if (r.Style != PdfPageLabelStyle.None && r.Start > 1)
        {
            sb.Append(CultureInfo.InvariantCulture, $" /St {r.Start}");
        }

        sb.Append(" >>");
    }

    private static string? StyleName(PdfPageLabelStyle style) => style switch
    {
        PdfPageLabelStyle.Arabic => "D",
        PdfPageLabelStyle.UpperRoman => "R",
        PdfPageLabelStyle.LowerRoman => "r",
        PdfPageLabelStyle.UpperLetters => "A",
        PdfPageLabelStyle.LowerLetters => "a",
        _ => null, // None — /S не пишется
    };

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
}
