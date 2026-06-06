using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Записывает настройки начального вида (<see cref="PdfViewerPreferences"/>) в исходные байты PDF
/// инкрементальным апдейтом (симметрично <see cref="PdfViewerPreferencesCosReader"/>'у). При наличии
/// хотя бы одного булева флага эмитит один объект <c>&lt;&lt; /HideToolbar true … &gt;&gt;</c> для
/// <c>/ViewerPreferences</c> и обновлённый catalog с <c>/PageLayout</c> / <c>/PageMode</c> /
/// <c>/ViewerPreferences N 0 R</c>, затем зовёт <see cref="PdfIncrementalWriter"/>. Оригинальные
/// байты не меняются (ISO 32000-1 §7.5.6).
/// </summary>
internal static class PdfViewerPreferencesCosWriter
{
    /// <summary>Все флаги <see langword="false"/> → подсловарь <c>/ViewerPreferences</c> не пишется
    /// (ключ опускается); раскладка / режим = <see cref="PdfPageLayout.Default"/> /
    /// <see cref="PdfPageMode.Default"/> → соответствующий catalog-ключ опускается. Возвращает байты
    /// нового PDF; <paramref name="source"/> не мутируется.</summary>
    public static byte[] Write(byte[] source, PdfViewerPreferences prefs)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prefs);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        long nextObj = MaxObjectNumber(doc) + 1;

        IReadOnlyList<PdfIncrementalWriter.RawObject> newObjects = [];
        IndirectReference? viewerPrefsRef = null;
        string? prefsBody = BuildViewerPreferencesDict(prefs);
        if (prefsBody is not null)
        {
            var prefsRef = new IndirectReference(nextObj++, 0);
            viewerPrefsRef = prefsRef;
            newObjects = [new PdfIncrementalWriter.RawObject(prefsRef, prefsBody)];
        }

        string catalogBody = PdfCatalogViewerPreferencesCosWriter.WriteCatalogWithViewerPreferences(
            catalog, prefs.PageLayout, prefs.PageMode, viewerPrefsRef);
        var updated = new[] { new PdfIncrementalWriter.RawObject(trailer.Root, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, newObjects, updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static string? BuildViewerPreferencesDict(PdfViewerPreferences prefs)
    {
        // PDF-default каждого флага — false, поэтому пишем только true-флаги. Если ни одного true —
        // подсловарь не нужен вовсе (возвращаем null → ключ опускается).
        var sb = new StringBuilder("<<");
        AppendBool(sb, "HideToolbar", prefs.HideToolbar);
        AppendBool(sb, "HideMenubar", prefs.HideMenubar);
        AppendBool(sb, "FitWindow", prefs.FitWindow);
        AppendBool(sb, "CenterWindow", prefs.CenterWindow);
        AppendBool(sb, "DisplayDocTitle", prefs.DisplayDocTitle);
        if (sb.Length == "<<".Length)
        {
            return null;
        }

        sb.Append(" >>");
        return sb.ToString();
    }

    private static void AppendBool(StringBuilder sb, string key, bool value)
    {
        if (value)
        {
            sb.Append(" /").Append(key).Append(" true");
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
}
