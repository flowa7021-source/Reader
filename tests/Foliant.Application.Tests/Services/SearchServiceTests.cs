using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class SearchServiceTests
{
    private readonly SearchService _sut = new(NullLogger<SearchService>.Instance);

    [Fact]
    public async Task EmptyQuery_ReturnsEmpty()
    {
        var doc = MakeDoc(["page one"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery(""), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NoMatch_ReturnsEmpty()
    {
        var doc = MakeDoc(["hello world"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("zzz"), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SingleMatch_OnSinglePage_ReturnsOneHit()
    {
        var doc = MakeDoc(["the quick brown fox jumps over the lazy dog"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("fox"), default);

        result.Should().HaveCount(1);
        result[0].PageIndex.Should().Be(0);
        result[0].Path.Should().Be("/x.pdf");
        result[0].Snippet.Should().Contain("fox");
    }

    [Fact]
    public async Task CaseInsensitive_FindsMatch()
    {
        var doc = MakeDoc(["The Quick Brown Fox"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("FOX"), default);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task MultipleMatches_OnSamePage_ReturnsAll()
    {
        var doc = MakeDoc(["foo bar foo baz foo qux"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("foo"), default);

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(h => h.PageIndex.Should().Be(0));
    }

    [Fact]
    public async Task MatchesAcrossPages_PreserveOrder()
    {
        var doc = MakeDoc(["alpha", "beta needle", "needle gamma"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("needle"), default);

        result.Should().HaveCount(2);
        result[0].PageIndex.Should().Be(1);
        result[1].PageIndex.Should().Be(2);
    }

    [Fact]
    public async Task MaxResults_CapsHitCount()
    {
        var doc = MakeDoc(["x x x x x x x x x x"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("x", MaxResults: 3), default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task NullTextLayer_PageIsSkipped()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(2);
        doc.GetTextLayerAsync(0, Arg.Any<CancellationToken>()).Returns((TextLayer?)null);
        doc.GetTextLayerAsync(1, Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<TextLayer?>(new TextLayer(1, [new TextRun("found", 0, 0, 100, 100)])));

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("found"), default);

        result.Should().HaveCount(1);
        result[0].PageIndex.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceled()
    {
        var doc = MakeDoc(["alpha", "beta"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("alpha"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Snippet_IncludesContextAround_Match()
    {
        var pageText = new string('a', 50) + "needle" + new string('b', 50);
        var doc = MakeDoc([pageText]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf", new SearchQuery("needle"), default);

        result.Should().HaveCount(1);
        result[0].Snippet.Should().Contain("needle");
        // Снипет должен содержать «...» при урезании.
        result[0].Snippet.Should().StartWith("...");
        result[0].Snippet.Should().EndWith("...");
    }

    // ───── Case-sensitive / whole-word options (S6/B) ─────

    [Fact]
    public async Task MatchCase_True_DoesNotFindLowercaseInUppercaseSource()
    {
        var doc = MakeDoc(["Hello WORLD"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("world", MatchCase: true), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchCase_False_DefaultIsCaseInsensitive()
    {
        var doc = MakeDoc(["Hello WORLD"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("world"), default);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task MatchWholeWord_True_RejectsSubstringMatches()
    {
        // "cat" is inside "category" and "catalog" but not whole-word — must not match.
        var doc = MakeDoc(["the category and catalog show a cat."]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("cat", MatchWholeWord: true), default);

        result.Should().HaveCount(1, "только последний 'cat.' окружён non-word characters");
    }

    [Fact]
    public async Task MatchWholeWord_False_FindsAllSubstrings()
    {
        var doc = MakeDoc(["the category and catalog show a cat."]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("cat"), default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task MatchWholeWord_HonorsBoundaries_AtStartAndEnd()
    {
        var doc = MakeDoc(["cat sits on the cat"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("cat", MatchWholeWord: true), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task MatchWholeWord_With_Underscore_TreatsAsWordChar()
    {
        // "_id" внутри "user_id" не должно матчиться при whole-word
        var doc = MakeDoc(["user_id and user id"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("id", MatchWholeWord: true), default);

        result.Should().HaveCount(1, "только 'id' после пробела матчится; 'user_id' — нет");
    }

    [Fact]
    public async Task BothOptionsCombined_Strict()
    {
        var doc = MakeDoc(["The Dog and the dog and DOG"]);

        var result = await _sut.SearchInDocumentAsync(doc, "/x.pdf",
            new SearchQuery("Dog", MatchCase: true, MatchWholeWord: true), default);

        result.Should().HaveCount(1, "только первый 'Dog' (case-sensitive whole-word)");
    }

    private static IDocument MakeDoc(string[] pageTexts)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(pageTexts.Length);

        for (int i = 0; i < pageTexts.Length; i++)
        {
            int pageIndex = i;
            string text = pageTexts[i];
            doc.GetTextLayerAsync(pageIndex, Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<TextLayer?>(
                   new TextLayer(pageIndex, [new TextRun(text, 0, 0, 100, 100)])));
        }
        return doc;
    }
}
