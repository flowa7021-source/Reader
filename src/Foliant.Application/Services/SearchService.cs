using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Application.Services;

public sealed class SearchService : ISearchService
{
    /// <summary>Сколько символов снипета слева/справа от матча.</summary>
    private const int SnippetContextChars = 30;

    private readonly ILogger<SearchService> _log;

    public SearchService(ILogger<SearchService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchInDocumentAsync(
        IDocument document,
        string documentPath,
        SearchQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentNullException.ThrowIfNull(query);

        if (query.IsEmpty || query.MaxResults <= 0)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        string needle = query.Text;

        for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();
            if (hits.Count >= query.MaxResults)
            {
                break;
            }

            TextLayer? layer = await document.GetTextLayerAsync(pageIndex, ct).ConfigureAwait(false);
            if (layer is null)
            {
                continue;
            }

            string pageText = layer.ToPlainText();
            CollectMatches(pageText, needle, pageIndex, documentPath, query, hits);
        }

        _log.LogDebug("Search '{Needle}' in '{Path}' returned {Count} hit(s) (capped at {Cap})",
            needle, documentPath, hits.Count, query.MaxResults);
        return hits;
    }

    private static void CollectMatches(
        string pageText,
        string needle,
        int pageIndex,
        string documentPath,
        SearchQuery query,
        List<SearchHit> hits)
    {
        if (string.IsNullOrEmpty(pageText))
        {
            return;
        }

        var comparison = query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int from = 0;
        while (hits.Count < query.MaxResults)
        {
            int pos = pageText.IndexOf(needle, from, comparison);
            if (pos < 0)
            {
                break;
            }

            if (!query.MatchWholeWord || IsWholeWordMatch(pageText, pos, needle.Length))
            {
                hits.Add(new SearchHit(
                    DocFingerprint: string.Empty,
                    Path: documentPath,
                    PageIndex: pageIndex,
                    Snippet: BuildSnippet(pageText, pos, needle.Length),
                    Rank: 1.0));
            }

            from = pos + needle.Length;
        }
    }

    /// <summary>Считаем «whole word» если соседние позиции либо вне строки, либо не letter/digit (Unicode-aware).</summary>
    private static bool IsWholeWordMatch(string text, int start, int len)
    {
        bool leftOk = start == 0 || !IsWordChar(text[start - 1]);
        int endIdx = start + len;
        bool rightOk = endIdx == text.Length || !IsWordChar(text[endIdx]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string BuildSnippet(string text, int matchStart, int matchLen)
    {
        int start = Math.Max(0, matchStart - SnippetContextChars);
        int end = Math.Min(text.Length, matchStart + matchLen + SnippetContextChars);

        string prefix = start > 0 ? "..." : string.Empty;
        string suffix = end < text.Length ? "..." : string.Empty;

        // Schiacciamo whitespace в snippet (text-layer часто содержит \n / \r после каждого run).
        string body = text[start..end].Replace('\n', ' ').Replace('\r', ' ').Trim();
        return $"{prefix}{body}{suffix}";
    }
}
