using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Удаляет document-level скрипты / действия (ISO 32000-1 §12.6 / §12.7), переписывая catalog
/// инкрементальным апдейтом (симметрично <see cref="PdfSanitizationCosReader"/>'у): JavaScript-
/// <c>/OpenAction</c>, ветка <c>/Names → /JavaScript</c> и catalog <c>/AA</c> убираются; безопасный
/// (GoTo / destination-array) <c>/OpenAction</c> и прочие подключи <c>/Names</c> сохраняются (логика
/// сохранения зеркалит <see cref="PdfCatalogNamesCosWriter"/>). Catalog-тело строит
/// <see cref="PdfCatalogSanitizationCosWriter"/>. Оригинальные байты не меняются (ISO 32000-1 §7.5.6).
/// </summary>
internal static class PdfSanitizationCosWriter
{
    private static readonly NameToken OpenActionName = NameToken.Create("OpenAction");
    private static readonly NameToken NamesName = NameToken.Create("Names");
    private static readonly NameToken JavaScriptName = NameToken.Create("JavaScript");
    private static readonly NameToken AaName = NameToken.Create("AA");
    private static readonly NameToken SName = NameToken.Create("S");

    /// <summary>
    /// Переписывает catalog без document-level скриптов / действий. <paramref name="removedAnything"/>
    /// = <see langword="true"/>, если был удалён хоть один из: JavaScript-<c>/OpenAction</c>,
    /// <c>/Names → /JavaScript</c>, catalog <c>/AA</c>; иначе <see langword="false"/> (но всё равно
    /// эмитится валидный, перезаписанный апдейт). Возвращает байты нового PDF;
    /// <paramref name="source"/> не мутируется.
    /// </summary>
    public static byte[] Remove(byte[] source, out bool removedAnything)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;

        bool dropOpenAction = HasJavaScriptOpenAction(doc, catalog);
        bool dropAa = catalog.ContainsKey(AaName);
        DictionaryToken? existingNames = ResolveCatalogNames(doc, catalog);
        bool dropJavaScriptNames = existingNames is not null && existingNames.ContainsKey(JavaScriptName);

        removedAnything = dropOpenAction || dropAa || dropJavaScriptNames;

        string catalogBody = PdfCatalogSanitizationCosWriter.WriteSanitizedCatalog(
            catalog, dropOpenAction, dropAa, existingNames);
        var updated = new[] { new PdfIncrementalWriter.RawObject(trailer.Root, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, [], updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static bool HasJavaScriptOpenAction(PdfPigDocument doc, DictionaryToken catalog)
    {
        // /OpenAction-скрипт — только action-словарь с /S == /JavaScript; destination ARRAY
        // ([3 0 R /Fit] — безопасный GoTo) не трогаем (зеркалит PdfSanitizationCosReader).
        if (!TryResolveDict(doc, catalog, OpenActionName, out var action))
        {
            return false;
        }

        return action.TryGet(SName, out NameToken? s) && s is not null &&
            string.Equals(s.Data, JavaScriptName.Data, StringComparison.Ordinal);
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
