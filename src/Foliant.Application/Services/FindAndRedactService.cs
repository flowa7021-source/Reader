using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Application.Services;

/// <summary>
/// Реализация find-and-redact wrapper'а: substring-путь делегирует <see cref="ISearchService"/>
/// (whole-word / fold-diacritics / case-sensitivity — реюз готовой логики); regex-путь читает
/// text layer напрямую и применяет .NET Regex к плоскому тексту страницы.
/// </summary>
public sealed class FindAndRedactService(
    ISearchService searchService,
    IRedactionService redactionService,
    ILogger<FindAndRedactService> log) : IFindAndRedactService
{
    private const int RegexTimeoutMs = 2000;
    private const int MaxRegexMatchesPerPage = 10_000;

    private readonly ISearchService _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    private readonly IRedactionService _redactionService = redactionService ?? throw new ArgumentNullException(nameof(redactionService));
    private readonly ILogger<FindAndRedactService> _log = log ?? throw new ArgumentNullException(nameof(log));

    public async Task<int> RedactMatchesAsync(
        IDocument document,
        string sourcePath,
        string targetPath,
        string query,
        FindAndRedactOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Query must be non-empty.", nameof(query));
        }

        IReadOnlyList<RedactionRegion> regions = options.Regex
            ? await CollectRegexRegionsAsync(document, query, options, ct).ConfigureAwait(false)
            : await CollectSubstringRegionsAsync(document, sourcePath, query, options, ct).ConfigureAwait(false);

        if (regions.Count == 0)
        {
            _log.LogDebug("Find-and-redact '{Query}' (regex={Regex}) in '{Src}' — 0 matches; output not written.",
                query, options.Regex, sourcePath);
            return 0;
        }

        await _redactionService.RedactAsync(sourcePath, targetPath, regions, ct).ConfigureAwait(false);
        _log.LogDebug("Find-and-redact '{Query}' (regex={Regex}) in '{Src}' — {Count} region(s) redacted to '{Dst}'.",
            query, options.Regex, sourcePath, regions.Count, targetPath);
        return regions.Count;
    }

    private async Task<IReadOnlyList<RedactionRegion>> CollectSubstringRegionsAsync(
        IDocument document, string sourcePath, string query, FindAndRedactOptions options, CancellationToken ct)
    {
        var searchQuery = new SearchQuery(
            Text: query,
            MaxResults: int.MaxValue,
            MatchCase: options.CaseSensitive,
            MatchWholeWord: options.WholeWord,
            FoldDiacritics: options.FoldDiacritics);

        IReadOnlyList<SearchHit> hits = await _searchService
            .SearchInDocumentAsync(document, sourcePath, searchQuery, ct)
            .ConfigureAwait(false);

        var regions = new List<RedactionRegion>(hits.Count);
        foreach (var hit in hits)
        {
            if (hit.Bbox is { } bbox)
            {
                regions.Add(new RedactionRegion(hit.PageIndex, bbox));
            }
        }
        return regions;
    }

    private static async Task<IReadOnlyList<RedactionRegion>> CollectRegexRegionsAsync(
        IDocument document, string pattern, FindAndRedactOptions options, CancellationToken ct)
    {
        var regex = BuildRegex(pattern, options);
        var regions = new List<RedactionRegion>();

        for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();
            TextLayer? layer = await document.GetTextLayerAsync(pageIndex, ct).ConfigureAwait(false);
            if (layer is null)
            {
                continue;
            }
            AppendRegexRegionsForPage(layer, pageIndex, regex, options.FoldDiacritics, regions);
        }
        return regions;
    }

    private static void AppendRegexRegionsForPage(
        TextLayer layer, int pageIndex, Regex regex, bool foldDiacritics, List<RedactionRegion> sink)
    {
        var index = RunIndex.Build(layer.Runs, foldDiacritics);
        if (index.PlainText.Length == 0)
        {
            return;
        }
        int safety = 0;
        foreach (Match m in regex.Matches(index.PlainText))
        {
            if (++safety > MaxRegexMatchesPerPage)
            {
                break;
            }
            if (m.Length == 0)
            {
                continue;
            }
            var run = index.RunForOffset(m.Index);
            sink.Add(new RedactionRegion(pageIndex, new AnnotationRect(run.X, run.Y, run.W, run.H)));
        }
    }

    private static Regex BuildRegex(string pattern, FindAndRedactOptions options)
    {
        var rxOptions = RegexOptions.CultureInvariant;
        if (!options.CaseSensitive)
        {
            rxOptions |= RegexOptions.IgnoreCase;
        }
        try
        {
            return new Regex(pattern, rxOptions, TimeSpan.FromMilliseconds(RegexTimeoutMs));
        }
        catch (ArgumentException ex)
        {
            // .NET бросает ArgumentException на невалидный шаблон — переоборачиваем в наш контракт.
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "Invalid regex pattern: {0}", ex.Message),
                nameof(pattern), ex);
        }
    }

    /// <summary>Плоский текст страницы + per-run start-offsets для разрешения match-position→bbox.</summary>
    private sealed class RunIndex
    {
        private readonly int[] _starts;
        private readonly IReadOnlyList<TextRun> _runs;

        private RunIndex(string plainText, int[] starts, IReadOnlyList<TextRun> runs)
        {
            PlainText = plainText;
            _starts = starts;
            _runs = runs;
        }

        public string PlainText { get; }

        public static RunIndex Build(IReadOnlyList<TextRun> runs, bool foldDiacritics)
        {
            var starts = new int[runs.Count];
            var sb = new StringBuilder();
            for (int i = 0; i < runs.Count; i++)
            {
                starts[i] = sb.Length;
                sb.Append(foldDiacritics ? DiacriticFolder.Fold(runs[i].Text) : runs[i].Text);
            }
            return new RunIndex(sb.ToString(), starts, runs);
        }

        public TextRun RunForOffset(int offset)
        {
            int lo = 0, hi = _starts.Length - 1, found = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (_starts[mid] <= offset) { found = mid; lo = mid + 1; }
                else { hi = mid - 1; }
            }
            return _runs[found];
        }
    }
}
