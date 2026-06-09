using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Infrastructure.Search;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// End-to-end search: the in-document substring search (<see cref="ISearchService"/>) and the
/// persistent FTS5 index (<see cref="IFtsIndex"/> via real SQLite) both find a word that genuinely
/// exists in the committed text PDF — proving the open → text-layer → index → query chain.
/// </summary>
[Trait("Category", "E2E")]
public sealed class SearchPipelineE2ETests
{
    [Fact]
    public async Task InDocumentSearch_FindsAWordFromThePdfTextLayer()
    {
        await using var host = new FoliantPipelineHost();
        string pdf = E2EFixtures.TextPdf();
        await using IDocument doc = await host.OpenAsync(pdf);

        string word = await FirstWordAsync(doc);

        IReadOnlyList<SearchHit> hits = await host.Get<ISearchService>()
            .SearchInDocumentAsync(doc, pdf, new SearchQuery(word), CancellationToken.None);

        hits.Should().NotBeEmpty("a word taken from the document's own text layer must be found");
        hits.Should().OnlyContain(h => h.PageIndex >= 0);
    }

    [Fact]
    public async Task FtsIndex_IndexThenSearch_ReturnsTheDocument()
    {
        await using var host = new FoliantPipelineHost();
        string pdf = E2EFixtures.TextPdf();
        await using IDocument doc = await host.OpenAsync(pdf);

        string fingerprint = await host.Get<IFileFingerprint>().ComputeAsync(pdf, CancellationToken.None);
        string word = await FirstWordAsync(doc);

        IFtsIndex fts = host.Get<IFtsIndex>();
        await fts.IndexDocumentAsync(fingerprint, pdf, StreamLayersAsync(doc), CancellationToken.None);

        IReadOnlyList<SearchHit> hits = await fts.SearchAsync(new SearchQuery(word), CancellationToken.None);

        hits.Should().Contain(h => h.DocFingerprint == fingerprint, "the indexed document should match its own word");

        IReadOnlyList<IndexedDocument> listed = await fts.ListAsync(CancellationToken.None);
        listed.Should().Contain(d => d.Fingerprint == fingerprint);
    }

    [Fact]
    public async Task FtsIndex_RemoveDocument_DropsItFromResults()
    {
        await using var host = new FoliantPipelineHost();
        string pdf = E2EFixtures.TextPdf();
        await using IDocument doc = await host.OpenAsync(pdf);

        string fingerprint = await host.Get<IFileFingerprint>().ComputeAsync(pdf, CancellationToken.None);
        string word = await FirstWordAsync(doc);

        IFtsIndex fts = host.Get<IFtsIndex>();
        await fts.IndexDocumentAsync(fingerprint, pdf, StreamLayersAsync(doc), CancellationToken.None);
        (await fts.RemoveDocumentAsync(fingerprint, CancellationToken.None)).Should().BeTrue();

        IReadOnlyList<SearchHit> hits = await fts.SearchAsync(new SearchQuery(word), CancellationToken.None);
        hits.Should().NotContain(h => h.DocFingerprint == fingerprint);
    }

    private static async IAsyncEnumerable<TextLayer> StreamLayersAsync(IDocument doc)
    {
        for (int i = 0; i < doc.PageCount; i++)
        {
            yield return await doc.GetTextLayerAsync(i, CancellationToken.None) ?? TextLayer.Empty(i);
        }
    }

    private static async Task<string> FirstWordAsync(IDocument doc)
    {
        TextLayer? layer = await doc.GetTextLayerAsync(0, CancellationToken.None);
        layer.Should().NotBeNull();
        string text = string.Join(' ', layer!.Runs.Select(r => r.Text));
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(w => w.Length >= 4 && w.All(char.IsLetter))
            ?? throw new InvalidOperationException("The text PDF has no searchable word in its first page layer.");
    }
}
