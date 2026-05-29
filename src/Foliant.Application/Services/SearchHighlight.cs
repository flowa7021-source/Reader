using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Stateless helper that maps search matches onto on-page rectangles so the UI can
/// highlight search hits over the rendered page. Because the PDF text layer is per-LINE
/// (each <see cref="TextRun"/> is one line with a bounding box and we have no per-character
/// coordinates), a match highlights the whole line whose text contains the query.
/// </summary>
public static class SearchHighlight
{
    /// <summary>
    /// Returns the bounding rectangle of every <see cref="TextRun"/> in <paramref name="layer"/>
    /// whose text contains <paramref name="query"/>, in document (run) order, with no dedup.
    /// Matching is case-insensitive unless <paramref name="matchCase"/> is <c>true</c>. A null,
    /// empty, or whitespace <paramref name="query"/> yields an empty list.
    /// </summary>
    /// <param name="layer">The page text layer to scan; must not be null.</param>
    /// <param name="query">The search query; must not be null.</param>
    /// <param name="matchCase">When <c>true</c>, comparison is case-sensitive (Ordinal).</param>
    /// <param name="matchWholeWord">When <c>true</c>, the query must appear delimited by
    /// non-alphanumeric boundaries — keeps highlights consistent with whole-word search results.</param>
    /// <param name="foldDiacritics">When <c>true</c>, both run text and query are NFD-folded
    /// (combining marks stripped) before comparison — keeps overlay highlights consistent with
    /// the search-results list under <see cref="SearchQuery.FoldDiacritics"/>.</param>
    /// <returns>The matching runs' rectangles in PDF points, preserving run order.</returns>
    public static IReadOnlyList<AnnotationRect> MatchRects(
        TextLayer layer, string query, bool matchCase = false, bool matchWholeWord = false, bool foldDiacritics = false)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string needle = foldDiacritics ? DiacriticFolder.Fold(query) : query;
        var rects = new List<AnnotationRect>();

        foreach (var run in layer.Runs)
        {
            string hay = foldDiacritics ? DiacriticFolder.Fold(run.Text) : run.Text;
            bool matches = matchWholeWord
                ? ContainsWord(hay, needle, comparison)
                : hay.Contains(needle, comparison);
            if (matches)
            {
                rects.Add(new AnnotationRect(run.X, run.Y, run.W, run.H));
            }
        }

        return rects;
    }

    private static bool ContainsWord(string text, string query, StringComparison comparison)
    {
        int index = text.IndexOf(query, comparison);
        while (index >= 0)
        {
            bool leftBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            int end = index + query.Length;
            bool rightBoundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftBoundary && rightBoundary)
            {
                return true;
            }
            index = text.IndexOf(query, index + 1, comparison);
        }
        return false;
    }
}
