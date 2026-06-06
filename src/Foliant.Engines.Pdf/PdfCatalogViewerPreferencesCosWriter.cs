using System.Globalization;
using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь, проставляя ключи начального вида: <c>/PageLayout</c>,
/// <c>/PageMode</c> (имена) и <c>/ViewerPreferences</c> (ссылка на подсловарь с флагами). Все прочие
/// ключи копируются как есть через <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально
/// <see cref="PdfCatalogPageLabelCosWriter"/>'у — отдельный writer, т.к. ключи и семантика разные.
/// </summary>
internal static class PdfCatalogViewerPreferencesCosWriter
{
    private const string PageLayoutKey = "PageLayout";
    private const string PageModeKey = "PageMode";
    private const string ViewerPreferencesKey = "ViewerPreferences";

    /// <summary>Копирует <paramref name="catalog"/>, перезаписывая ключи начального вида. Раскладка /
    /// режим, равные <see cref="PdfPageLayout.Default"/> / <see cref="PdfPageMode.Default"/>,
    /// опускаются; <paramref name="viewerPrefsRef"/> = <see langword="null"/> опускает
    /// <c>/ViewerPreferences</c>, иначе пишется <c>/ViewerPreferences N 0 R</c>.</summary>
    public static string WriteCatalogWithViewerPreferences(
        DictionaryToken catalog,
        PdfPageLayout layout,
        PdfPageMode mode,
        IndirectReference? viewerPrefsRef)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in catalog.Data)
        {
            if (string.Equals(kv.Key, PageLayoutKey, StringComparison.Ordinal) ||
                string.Equals(kv.Key, PageModeKey, StringComparison.Ordinal) ||
                string.Equals(kv.Key, ViewerPreferencesKey, StringComparison.Ordinal))
            {
                continue; // переписываем (или опускаем) ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        string? layoutName = LayoutName(layout);
        if (layoutName is not null)
        {
            sb.Append("/PageLayout /").Append(layoutName).Append('\n');
        }

        string? modeName = ModeName(mode);
        if (modeName is not null)
        {
            sb.Append("/PageMode /").Append(modeName).Append('\n');
        }

        if (viewerPrefsRef is { } prefs)
        {
            sb.Append(CultureInfo.InvariantCulture, $"/ViewerPreferences {prefs.ObjectNumber} {prefs.Generation} R\n");
        }

        sb.Append(">>");
        return sb.ToString();
    }

    private static string? LayoutName(PdfPageLayout layout) => layout switch
    {
        PdfPageLayout.SinglePage => "SinglePage",
        PdfPageLayout.OneColumn => "OneColumn",
        PdfPageLayout.TwoColumnLeft => "TwoColumnLeft",
        PdfPageLayout.TwoColumnRight => "TwoColumnRight",
        PdfPageLayout.TwoPageLeft => "TwoPageLeft",
        PdfPageLayout.TwoPageRight => "TwoPageRight",
        _ => null, // Default — /PageLayout не пишется
    };

    private static string? ModeName(PdfPageMode mode) => mode switch
    {
        PdfPageMode.UseNone => "UseNone",
        PdfPageMode.UseOutlines => "UseOutlines",
        PdfPageMode.UseThumbs => "UseThumbs",
        PdfPageMode.FullScreen => "FullScreen",
        PdfPageMode.UseOC => "UseOC",
        PdfPageMode.UseAttachments => "UseAttachments",
        _ => null, // Default — /PageMode не пишется
    };
}
