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

            CollectMatches(layer, needle, pageIndex, documentPath, query, hits);
        }

        _log.LogDebug("Search '{Needle}' in '{Path}' returned {Count} hit(s) (capped at {Cap})",
            needle, documentPath, hits.Count, query.MaxResults);
        return hits;
    }

    private static void CollectMatches(
        TextLayer layer,
        string needle,
        int pageIndex,
        string documentPath,
        SearchQuery query,
        List<SearchHit> hits)
    {
        // FoldDiacritics: нормализуем обе стороны к NFD и стрипим combining marks (Mn). После
        // этого "café" и "cafe" совпадут, но positional offset в нормализованной строке != в
        // оригинальной → снипет тоже строим из нормализованного pageText, чтобы UI не плыл.
        // Trade-off: пользователь видит снипет без диакритики; он явно opt-in'ался на это.
        var runs = BuildRunOffsets(layer.Runs, query.FoldDiacritics);
        string pageText = runs.PlainText;
        if (pageText.Length == 0)
        {
            return;
        }

        string needlePlain = query.FoldDiacritics ? DiacriticFolder.Fold(needle) : needle;
        var comparison = query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int from = 0;
        while (hits.Count < query.MaxResults)
        {
            int pos = pageText.IndexOf(needlePlain, from, comparison);
            if (pos < 0)
            {
                break;
            }

            if (!query.MatchWholeWord || IsWholeWordMatch(pageText, pos, needlePlain.Length))
            {
                hits.Add(new SearchHit(
                    DocFingerprint: string.Empty,
                    Path: documentPath,
                    PageIndex: pageIndex,
                    Snippet: BuildSnippet(pageText, pos, needlePlain.Length),
                    Rank: 1.0,
                    Bbox: runs.RectForOffset(pos)));
            }

            from = pos + needlePlain.Length;
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

    /// <summary>
    /// Плоская строка страницы + per-run offsets для разрешения match→bbox. Если match попадает
    /// в run, используется bbox этого run'а (PDF text layer пока пер-строчный — sub-character
    /// координаты недоступны). При <c>foldDiacritics</c> длины могут отличаться от оригинала,
    /// но и pageText и offsets строятся из folded версии — индексы согласованы.
    /// </summary>
    private static RunOffsets BuildRunOffsets(IReadOnlyList<TextRun> runs, bool foldDiacritics)
    {
        var starts = new int[runs.Count];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < runs.Count; i++)
        {
            starts[i] = sb.Length;
            sb.Append(foldDiacritics ? DiacriticFolder.Fold(runs[i].Text) : runs[i].Text);
        }
        return new RunOffsets(sb.ToString(), starts, runs);
    }

    private sealed class RunOffsets(string plainText, int[] starts, IReadOnlyList<TextRun> runs)
    {
        public string PlainText { get; } = plainText;

        public AnnotationRect? RectForOffset(int offset)
        {
            if (runs.Count == 0)
            {
                return null;
            }
            // Binary search: starts[] монотонно неубывающий — найти крайний правый run с start ≤ offset.
            int lo = 0, hi = starts.Length - 1, found = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (starts[mid] <= offset) { found = mid; lo = mid + 1; }
                else { hi = mid - 1; }
            }
            var run = runs[found];
            return new AnnotationRect(run.X, run.Y, run.W, run.H);
        }
    }
}
