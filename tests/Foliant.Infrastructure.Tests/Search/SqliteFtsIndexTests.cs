using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Search;

[Trait("Category", "Integration")]
public sealed class SqliteFtsIndexTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly SqliteFtsIndex _sut;

    public SqliteFtsIndexTests()
    {
        _sut = new SqliteFtsIndex(_tmp.File("fts.db"), NullLogger<SqliteFtsIndex>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _tmp.Dispose();
    }

    [Fact]
    public async Task IndexThenSearch_FindsHit()
    {
        await IndexAsync("doc-A", "/path/A.pdf",
            (0, "Привет, мир. Foliant — это PDF-просмотрщик."),
            (1, "Здесь живёт PDFium."));

        var hits = await _sut.SearchAsync(new SearchQuery("PDFium"), default);

        hits.Should().HaveCount(1);
        hits[0].Path.Should().Be("/path/A.pdf");
        hits[0].PageIndex.Should().Be(1);
        hits[0].Snippet.Should().Contain("[PDFium]");
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        await IndexAsync("doc-A", "/p", (0, "anything"));

        var hits = await _sut.SearchAsync(new SearchQuery(""), default);
        hits.Should().BeEmpty();

        hits = await _sut.SearchAsync(new SearchQuery("   "), default);
        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_RestrictToDoc_ReturnsOnlyThatDoc()
    {
        await IndexAsync("doc-A", "/A.pdf", (0, "Foliant rocks"));
        await IndexAsync("doc-B", "/B.pdf", (0, "Foliant also rocks here"));

        var allHits = await _sut.SearchAsync(new SearchQuery("Foliant"), default);
        allHits.Should().HaveCount(2);

        var restricted = await _sut.SearchAsync(new SearchQuery("Foliant", RestrictToDocFingerprint: "doc-B"), default);
        restricted.Should().ContainSingle().Which.Path.Should().Be("/B.pdf");
    }

    [Fact]
    public async Task Search_MaxResults_Limits()
    {
        for (var i = 0; i < 20; i++)
        {
            await IndexAsync($"doc-{i}", $"/p{i}.pdf", (0, "Foliant"));
        }

        var hits = await _sut.SearchAsync(new SearchQuery("Foliant", MaxResults: 5), default);

        hits.Should().HaveCount(5);
    }

    [Fact]
    public async Task ReindexSameDoc_ReplacesOldPages()
    {
        await IndexAsync("doc-A", "/A.pdf", (0, "old text"));
        await IndexAsync("doc-A", "/A.pdf", (0, "new text"));

        var hitsOld = await _sut.SearchAsync(new SearchQuery("old"), default);
        hitsOld.Should().BeEmpty();

        var hitsNew = await _sut.SearchAsync(new SearchQuery("new"), default);
        hitsNew.Should().ContainSingle();
    }

    [Fact]
    public async Task RemoveDocument_DropsAllItsHits_KeepsOthers()
    {
        await IndexAsync("doc-A", "/A.pdf", (0, "Foliant"));
        await IndexAsync("doc-B", "/B.pdf", (0, "Foliant"));

        var removed = await _sut.RemoveDocumentAsync("doc-A", default);
        removed.Should().BeTrue();

        var hits = await _sut.SearchAsync(new SearchQuery("Foliant"), default);
        hits.Should().ContainSingle().Which.Path.Should().Be("/B.pdf");
    }

    [Fact]
    public async Task RemoveDocument_Missing_ReturnsFalse()
    {
        var removed = await _sut.RemoveDocumentAsync("not-indexed", default);
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_ReturnsIndexedDocs_OrderedByLastIndexedDesc()
    {
        await IndexAsync("doc-A", "/A.pdf", (0, "x"));
        await Task.Delay(2);
        await IndexAsync("doc-B", "/B.pdf", (0, "x"));

        var list = await _sut.ListAsync(default);

        list.Should().HaveCount(2);
        list[0].Fingerprint.Should().Be("doc-B");
        list[0].PageCount.Should().Be(1);
    }

    [Fact]
    public async Task Diacritics_AreIgnored_ByTokenizer()
    {
        await IndexAsync("doc", "/p", (0, "café résumé naïve"));

        var hits = await _sut.SearchAsync(new SearchQuery("cafe"), default);

        hits.Should().NotBeEmpty();
    }

    private async Task IndexAsync(string fp, string path, params (int Page, string Text)[] pages)
    {
        await _sut.IndexDocumentAsync(fp, path, AsyncEnumerablePages(pages), default);
    }

    private static async IAsyncEnumerable<TextLayer> AsyncEnumerablePages((int Page, string Text)[] pages)
    {
        foreach (var (page, text) in pages)
        {
            yield return new TextLayer(page, [new TextRun(text, 0, 0, 100, 12)]);
            await Task.Yield();
        }
    }
}
