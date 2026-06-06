using System.Globalization;
using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Добавляет / удаляет именованные пункты назначения, переписывая catalog name-tree
/// <c>/Names → /Dests</c> инкрементальным апдейтом (симметрично
/// <see cref="PdfNamedDestinationCosReader"/>'у). Существующие modern-записи (имя → целевая страница)
/// сохраняются — в плоский <c>/Names</c>-массив заново выписываются их <c>[pageRef /Fit]</c> +
/// добавляемая/удаляемая запись; ключи сортируются ordinal'но (name-tree требует упорядоченных ключей,
/// ISO 32000-1 §7.9.6). Запись идёт только в modern-форму: legacy-словарь catalog <c>/Dests</c> не
/// трогается (см. MVP-ограничение в <c>IPdfNamedDestinationService.RemoveAsync</c>). Оригинальные байты
/// не меняются (ISO 32000-1 §7.5.6).
/// </summary>
internal static class PdfNamedDestinationCosWriter
{
    private static readonly NameToken NamesName = NameToken.Create("Names");

    /// <summary>
    /// Добавляет / заменяет пункт назначения <paramref name="name"/> → <c>[pageRef /Fit]</c>, где
    /// pageRef — страница с индексом <paramref name="pageIndex"/>, зажатым в <c>[0, pageCount-1]</c>.
    /// Существующие modern-записи сохраняются; коллизия по имени → старая запись заменяется новой.
    /// Возвращает байты нового PDF; <paramref name="source"/> не мутируется.
    /// </summary>
    public static byte[] Add(byte[] source, string name, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var doc = PdfPigDocument.Open(source);
        var pageRefs = CollectPageRefs(doc);
        int clamped = Math.Clamp(pageIndex, 0, pageRefs.Count - 1);

        var entries = ExistingExcept(doc, name, pageRefs);
        entries.Add((name, pageRefs[clamped]));
        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        return EmitNameTree(source, doc, entries);
    }

    /// <summary>
    /// Удаляет пункт назначения с именем <paramref name="name"/> из modern name-tree (no-op, если его
    /// там нет — но всё равно эмитит валидный апдейт). Возвращает байты нового PDF;
    /// <paramref name="source"/> не мутируется.
    /// </summary>
    public static byte[] Remove(byte[] source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var doc = PdfPigDocument.Open(source);
        var pageRefs = CollectPageRefs(doc);

        var entries = ExistingExcept(doc, name, pageRefs);
        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        return EmitNameTree(source, doc, entries);
    }

    private static List<(string Key, IndirectReference Ref)> ExistingExcept(
        PdfPigDocument doc, string name, List<IndirectReference> pageRefs)
    {
        // Сохраняем только modern-записи (legacy не мигрируем); каждую переэмитим как [pageRef /Fit],
        // поэтому индекс страницы превращаем обратно в pageRef. Индекс из reader'а уже в [0, count-1].
        var existing = PdfNamedDestinationCosReader.ReadModern(doc);
        var entries = new List<(string Key, IndirectReference Ref)>(existing.Count + 1);
        foreach (var e in existing)
        {
            if (string.Equals(e.Name, name, StringComparison.Ordinal))
            {
                continue; // коллизия по имени (Add) либо удаляемый ключ (Remove)
            }

            int idx = Math.Clamp(e.PageIndex, 0, pageRefs.Count - 1);
            entries.Add((e.Name, pageRefs[idx]));
        }

        return entries;
    }

    private static byte[] EmitNameTree(
        byte[] source, PdfPigDocument doc, List<(string Key, IndirectReference Ref)> entries)
    {
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        long nextObj = MaxObjectNumber(doc) + 1;

        var nameTreeRef = new IndirectReference(nextObj++, 0);
        var namesDictRef = new IndirectReference(nextObj++, 0);

        var newObjects = new List<PdfIncrementalWriter.RawObject>(2)
        {
            new(nameTreeRef, WriteDestsTree(entries)),
        };

        DictionaryToken? existingNames = ResolveCatalogNames(doc, catalog);
        newObjects.Add(new PdfIncrementalWriter.RawObject(
            namesDictRef, PdfCatalogNamedDestinationsCosWriter.WriteNamesDict(existingNames, nameTreeRef)));

        string catalogBody = PdfCatalogNamedDestinationsCosWriter.WriteCatalogWithNames(catalog, namesDictRef);
        var updated = new[] { new PdfIncrementalWriter.RawObject(trailer.Root, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, newObjects, updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static string WriteDestsTree(List<(string Key, IndirectReference Ref)> entries)
    {
        // Свежий name-tree-лист: << /Names [ (key) [pageRef /Fit] … ] >>. Пустой список → /Names []
        // (валидно). Значение — inline destination-массив, поэтому отдельные объекты не нужны.
        var sb = new StringBuilder("<<\n/Names [");
        foreach (var (key, reference) in entries)
        {
            sb.Append(' ');
            PdfTextString.Append(sb, key);
            sb.Append(CultureInfo.InvariantCulture,
                $" [{reference.ObjectNumber} {reference.Generation} R /Fit]");
        }

        sb.Append(" ]\n>>");
        return sb.ToString();
    }

    private static List<IndirectReference> CollectPageRefs(PdfPigDocument doc)
    {
        var refs = new List<IndirectReference>();
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (catalog.TryGet(NameToken.Pages, out IndirectReferenceToken? pagesRef) && pagesRef is not null)
        {
            WalkPages(doc, pagesRef.Data, refs);
        }

        if (refs.Count == 0)
        {
            throw new InvalidDataException("PDF has no page objects; cannot anchor named destinations.");
        }

        return refs;
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

    private static DictionaryToken? ResolveCatalogNames(PdfPigDocument doc, DictionaryToken catalog)
    {
        if (!catalog.TryGet(NamesName, out IToken? raw) || raw is null)
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
