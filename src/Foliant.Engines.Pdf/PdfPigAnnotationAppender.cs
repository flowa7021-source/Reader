using Foliant.Domain;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// cos-level пост-процессор для Line/Arrow/Polygon аннотаций (Q-F17 11/11). PDFium 146.x не
/// экспонирует setter'ы для <c>/L</c>/<c>/Vertices</c>/<c>/LE</c>, поэтому после того как
/// <see cref="AnnotatedPdfExportService"/> сохранил PDF через PDFium (8/11 типов), этот аппендер
/// читает результат PdfPig'ом, находит IndirectReference каждой нужной страницы и приклеивает к
/// PDF <b>инкрементальный апдейт</b>: новые annotation-объекты + обновлённое page-словарь с
/// расширенным <c>/Annots</c> массивом + новая xref-секция с <c>/Prev</c>.
///
/// Инкрементальный апдейт — стандартный механизм PDF spec (ISO 32000-1 §7.5.6): оригинальные
/// байты не меняются, новые объекты дописываются в конец, новая xref ссылается на старую через
/// <c>/Prev</c>. Reader (Acrobat/PDFium/etc.) при загрузке видит самую свежую версию каждого
/// объекта. Так annotations Line/Arrow/Polygon становятся native PDF-объектами без зависимости
/// от того, какие setter'ы экспонирует PDFium.
///
/// Только Line/Arrow/Polygon — остальные 8 типов уже работают через PDFium и не трогаются.
/// Сама запись байт делает <see cref="PdfIncrementalWriter"/>; маппинг annotation→cos-text
/// делает <see cref="PdfAnnotationCosWriter"/>; rewrite page-словаря — <see cref="PdfDictionaryCosWriter"/>.
/// </summary>
public static class PdfPigAnnotationAppender
{
    /// <summary>
    /// Принимает байты PDF (например, вывод <c>FPDF_SaveAsCopy</c>) + список аннотаций. Если среди
    /// них нет Line/Arrow/Polygon — возвращает <paramref name="pdfBytes"/> без изменений. Иначе
    /// возвращает новые байты с инкрементальным апдейтом, в котором страницы расширены
    /// <c>/Annots</c> для соответствующих типов.
    /// </summary>
    public static byte[] AppendLinePolygonAnnotations(byte[] pdfBytes, IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentNullException.ThrowIfNull(annotations);

        var byPage = GroupSupportedByPage(annotations);
        if (byPage.Count == 0)
        {
            return pdfBytes;
        }

        var docInfo = ReadDocInfo(pdfBytes, byPage.Keys);
        if (docInfo.PageRefs.Count == 0)
        {
            return pdfBytes;
        }

        long nextObjectNumber = docInfo.NextObjectNumber;
        var plan = BuildIncrementalPlan(docInfo.PageRefs, byPage, ref nextObjectNumber);
        long prevXrefOffset = PdfIncrementalWriter.FindLastStartXref(pdfBytes);
        return PdfIncrementalWriter.Append(
            pdfBytes,
            plan.NewObjects,
            plan.UpdatedPages,
            prevXrefOffset,
            docInfo.TrailerRootObj,
            docInfo.TrailerSize);
    }

    // --- planning -----------------------------------------------------------

    private static SortedDictionary<int, List<Annotation>> GroupSupportedByPage(IReadOnlyList<Annotation> annotations)
    {
        var result = new SortedDictionary<int, List<Annotation>>();
        foreach (var a in annotations)
        {
            if (a.Kind is not (AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Polygon))
            {
                continue;
            }

            if (!IsValid(a))
            {
                continue;
            }

            if (!result.TryGetValue(a.PageIndex, out var list))
            {
                list = [];
                result[a.PageIndex] = list;
            }

            list.Add(a);
        }

        return result;
    }

    private static bool IsValid(Annotation a) => a.Kind switch
    {
        AnnotationKind.Line or AnnotationKind.Arrow => a.InkPoints is { Count: 2 },
        AnnotationKind.Polygon => a.InkPoints is { Count: >= 3 },
        _ => false,
    };

    private static DocInfo ReadDocInfo(byte[] pdfBytes, IEnumerable<int> wantedPageIndices)
    {
        var wanted = new HashSet<int>(wantedPageIndices);
        var refs = new List<PageRefAndAnnots>(wanted.Count);
        using var doc = PdfPigDocument.Open(pdfBytes);
        int pageIndex = 0;
        foreach (var (reference, dict) in WalkPageLeaves(doc))
        {
            if (wanted.Contains(pageIndex))
            {
                ArrayToken existing = ExtractExistingAnnotsArray(doc, dict);
                refs.Add(new PageRefAndAnnots(pageIndex, reference, dict, existing));
            }

            pageIndex++;
        }

        long maxObj = 0;
        foreach (var key in doc.Structure.CrossReferenceTable.ObjectOffsets.Keys)
        {
            if (key.ObjectNumber > maxObj)
            {
                maxObj = key.ObjectNumber;
            }
        }

        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        return new DocInfo(refs, maxObj + 1, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static IEnumerable<(IndirectReference Reference, DictionaryToken Dict)> WalkPageLeaves(PdfPigDocument doc)
    {
        // Catalog → /Pages (indirect ref на root node) → рекурсивный обход /Kids. Узлы с
        // /Type /Page — листья; /Type /Pages — промежуточные.
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) || pagesRef is null)
        {
            yield break;
        }

        foreach (var leaf in WalkSubtree(doc, pagesRef.Data))
        {
            yield return leaf;
        }
    }

    private static IEnumerable<(IndirectReference Reference, DictionaryToken Dict)> WalkSubtree(
        PdfPigDocument doc, IndirectReference nodeRef)
    {
        if (doc.Structure.GetObject(nodeRef) is not ObjectToken { Data: DictionaryToken node })
        {
            yield break;
        }

        // /Type /Page = лист; /Pages = промежуточный, дальше через /Kids.
        if (node.TryGet(NameToken.Type, out NameToken? type) && type is { } t &&
            string.Equals(t.Data, NameToken.Page.Data, StringComparison.Ordinal))
        {
            yield return (nodeRef, node);
            yield break;
        }

        if (!node.TryGet(NameToken.Kids, out ArrayToken? kids) || kids is null)
        {
            yield break;
        }

        foreach (var kid in kids.Data)
        {
            if (kid is not IndirectReferenceToken kref)
            {
                continue;
            }

            foreach (var leaf in WalkSubtree(doc, kref.Data))
            {
                yield return leaf;
            }
        }
    }

    private static ArrayToken ExtractExistingAnnotsArray(PdfPigDocument doc, DictionaryToken pageDict)
    {
        if (!pageDict.TryGet(NameToken.Annots, out IToken? rawAnnots))
        {
            return new ArrayToken([]);
        }

        // /Annots может быть как inline-массивом, так и indirect-ссылкой; во втором случае
        // резолвим её через TokenScanner.
        return rawAnnots switch
        {
            ArrayToken arr => arr,
            IndirectReferenceToken iref => doc.Structure.GetObject(iref.Data) is ObjectToken { Data: ArrayToken resolved }
                ? resolved
                : new ArrayToken([]),
            _ => new ArrayToken([]),
        };
    }

    private static IncrementalPlan BuildIncrementalPlan(
        IReadOnlyList<PageRefAndAnnots> pages,
        SortedDictionary<int, List<Annotation>> byPage,
        ref long nextObjectNumber)
    {
        var newObjects = new List<PdfIncrementalWriter.RawObject>();
        var updatedPages = new List<PdfIncrementalWriter.RawObject>();

        foreach (var page in pages)
        {
            if (!byPage.TryGetValue(page.PageIndex, out var annots) || annots.Count == 0)
            {
                continue;
            }

            var newRefs = new List<IndirectReference>(annots.Count);
            foreach (var a in annots)
            {
                long num = nextObjectNumber++;
                var iref = new IndirectReference(num, 0);
                string body = PdfAnnotationCosWriter.WriteAnnotationObject(a, page.Reference);
                newObjects.Add(new PdfIncrementalWriter.RawObject(iref, body));
                newRefs.Add(iref);
            }

            string pageBody = PdfDictionaryCosWriter.WritePageDictWithExtendedAnnots(
                page.PageDict, page.ExistingAnnots, newRefs);
            updatedPages.Add(new PdfIncrementalWriter.RawObject(page.Reference, pageBody));
        }

        return new IncrementalPlan(newObjects, updatedPages);
    }

    // --- internal records ---------------------------------------------------

    private sealed record DocInfo(
        IReadOnlyList<PageRefAndAnnots> PageRefs,
        long NextObjectNumber,
        long TrailerRootObj,
        long TrailerSize);

    private sealed record PageRefAndAnnots(
        int PageIndex,
        IndirectReference Reference,
        DictionaryToken PageDict,
        ArrayToken ExistingAnnots);

    private sealed record IncrementalPlan(
        IReadOnlyList<PdfIncrementalWriter.RawObject> NewObjects,
        IReadOnlyList<PdfIncrementalWriter.RawObject> UpdatedPages);
}
