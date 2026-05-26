using System.Globalization;
using BenchmarkDotNet.Attributes;
using Foliant.Domain;
using Foliant.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foliant.Performance;

[MemoryDiagnoser]
[BenchmarkCategory("CrossPlatform")]
public class FtsSearchBenchmarks
{
    private const int DocCount = 10;
    private const int PagesPerDoc = 1000;

    private string _dbDir = null!;
    private SqliteFtsIndex _index = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "foliant-bench-fts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _index = new SqliteFtsIndex(Path.Combine(_dbDir, "fts.db"), NullLogger<SqliteFtsIndex>.Instance);

        for (var doc = 0; doc < DocCount; doc++)
        {
            _index.IndexDocumentAsync(
                $"doc-{doc}",
                $"/synthetic/doc-{doc}.pdf",
                SyntheticPages(doc),
                CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _index.Dispose();
        try
        {
            Directory.Delete(_dbDir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup of the temp index
        }
    }

    [Benchmark]
    public async Task<int> SearchAcross10kPages()
    {
        var hits = await _index.SearchAsync(new SearchQuery("needle", MaxResults: 50), CancellationToken.None);
        return hits.Count;
    }

    private static async IAsyncEnumerable<TextLayer> SyntheticPages(int doc)
    {
        for (var page = 0; page < PagesPerDoc; page++)
        {
            // A few pages per document carry the search term so the query has real hits to rank.
            var hasNeedle = page % 250 == 0;
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"document {doc} page {page} lorem ipsum dolor sit amet consectetur {(hasNeedle ? "needle" : "filler")} alpha beta gamma delta");
            yield return new TextLayer(page, [new TextRun(text, 0, 0, 100, 12)]);
            await Task.Yield();
        }
    }
}
