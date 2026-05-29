namespace Foliant.Application.Services;

/// <summary>
/// Stateless Unicode diacritic-folding helper. NFD-normalises the input and strips combining
/// marks (Unicode category <c>NonSpacingMark</c>): "café" → "cafe", "Ñ" → "N", "ё" → "е".
/// Shared by <see cref="ISearchService"/> implementations and <see cref="SearchHighlight"/>,
/// so the search-results list and the overlay highlights stay consistent under the
/// fold-diacritics search option.
/// </summary>
public static class DiacriticFolder
{
    /// <summary>Fold the input. <c>null</c> in → <c>null</c> out; empty in → empty out.</summary>
    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return value;
        }

        string nfd = value.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(nfd.Length);
        foreach (char c in nfd)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
