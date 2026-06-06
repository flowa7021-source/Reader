using Foliant.Domain;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает настройки начального вида из cos-структуры PDF: catalog-ключи <c>/PageLayout</c> и
/// <c>/PageMode</c> (NameToken) + подсловарь <c>/ViewerPreferences</c> с булевыми флагами
/// (<c>/HideToolbar</c>, <c>/HideMenubar</c>, <c>/FitWindow</c>, <c>/CenterWindow</c>,
/// <c>/DisplayDocTitle</c>). Симметрично <see cref="PdfViewerPreferencesCosWriter"/>'у. Отсутствующие
/// ключи / флаги → <see cref="PdfPageLayout.Default"/> / <see cref="PdfPageMode.Default"/> /
/// <see langword="false"/>.
/// </summary>
internal static class PdfViewerPreferencesCosReader
{
    private static readonly NameToken PageLayoutName = NameToken.Create("PageLayout");
    private static readonly NameToken PageModeName = NameToken.Create("PageMode");
    private static readonly NameToken ViewerPreferencesName = NameToken.Create("ViewerPreferences");
    private static readonly NameToken HideToolbarName = NameToken.Create("HideToolbar");
    private static readonly NameToken HideMenubarName = NameToken.Create("HideMenubar");
    private static readonly NameToken FitWindowName = NameToken.Create("FitWindow");
    private static readonly NameToken CenterWindowName = NameToken.Create("CenterWindow");
    private static readonly NameToken DisplayDocTitleName = NameToken.Create("DisplayDocTitle");

    /// <summary>Читает настройки начального вида. Нет ключей / повреждённая секция →
    /// <see cref="PdfViewerPreferences.Default"/>.</summary>
    public static PdfViewerPreferences Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;

        PdfPageLayout layout = ReadPageLayout(catalog);
        PdfPageMode mode = ReadPageMode(catalog);

        bool hideToolbar = false;
        bool hideMenubar = false;
        bool fitWindow = false;
        bool centerWindow = false;
        bool displayDocTitle = false;
        if (TryResolveDict(doc, catalog, ViewerPreferencesName, out var prefs))
        {
            hideToolbar = ReadBool(prefs, HideToolbarName);
            hideMenubar = ReadBool(prefs, HideMenubarName);
            fitWindow = ReadBool(prefs, FitWindowName);
            centerWindow = ReadBool(prefs, CenterWindowName);
            displayDocTitle = ReadBool(prefs, DisplayDocTitleName);
        }

        return new PdfViewerPreferences(
            layout, mode, hideToolbar, hideMenubar, fitWindow, centerWindow, displayDocTitle);
    }

    private static PdfPageLayout ReadPageLayout(DictionaryToken catalog)
    {
        if (catalog.TryGet(PageLayoutName, out NameToken? name) && name is not null)
        {
            return MapLayout(name.Data);
        }

        return PdfPageLayout.Default;
    }

    private static PdfPageMode ReadPageMode(DictionaryToken catalog)
    {
        if (catalog.TryGet(PageModeName, out NameToken? name) && name is not null)
        {
            return MapMode(name.Data);
        }

        return PdfPageMode.Default;
    }

    private static PdfPageLayout MapLayout(string name) => name switch
    {
        "SinglePage" => PdfPageLayout.SinglePage,
        "OneColumn" => PdfPageLayout.OneColumn,
        "TwoColumnLeft" => PdfPageLayout.TwoColumnLeft,
        "TwoColumnRight" => PdfPageLayout.TwoColumnRight,
        "TwoPageLeft" => PdfPageLayout.TwoPageLeft,
        "TwoPageRight" => PdfPageLayout.TwoPageRight,
        _ => PdfPageLayout.Default, // неизвестное значение → default
    };

    private static PdfPageMode MapMode(string name) => name switch
    {
        "UseNone" => PdfPageMode.UseNone,
        "UseOutlines" => PdfPageMode.UseOutlines,
        "UseThumbs" => PdfPageMode.UseThumbs,
        "FullScreen" => PdfPageMode.FullScreen,
        "UseOC" => PdfPageMode.UseOC,
        "UseAttachments" => PdfPageMode.UseAttachments,
        _ => PdfPageMode.Default, // неизвестное значение → default
    };

    private static bool ReadBool(DictionaryToken dict, NameToken key) =>
        dict.TryGet(key, out BooleanToken? b) && b is not null && b.Data;

    private static bool TryResolveDict(
        UglyToad.PdfPig.PdfDocument doc, DictionaryToken parent, NameToken key, out DictionaryToken dict)
    {
        dict = null!;
        if (!parent.TryGet(key, out IToken? raw) || raw is null)
        {
            return false;
        }

        return TryAsDict(doc, raw, out dict);
    }

    private static bool TryAsDict(UglyToad.PdfPig.PdfDocument doc, IToken raw, out DictionaryToken dict)
    {
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
                dict = null!;
                return false;
        }
    }
}
