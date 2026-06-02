using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class FindAndRedactServiceTests
{
    private const string SrcPath = "/in.pdf";
    private const string DstPath = "/out.pdf";

    private readonly IRedactionService _redact = Substitute.For<IRedactionService>();
    private readonly FindAndRedactService _sut;

    public FindAndRedactServiceTests()
    {
        var realSearch = new SearchService(NullLogger<SearchService>.Instance);
        _sut = new FindAndRedactService(realSearch, _redact, NullLogger<FindAndRedactService>.Instance);
    }

    [Fact]
    public async Task Substring_FindsAllOccurrences_PassesNRegionsToRedactionService()
    {
        // 3 runs containing "foo", 1 not. Expect 3 regions in document order.
        var doc = MakeDoc(
            new TextRun("foo bar", 1, 1, 10, 10),
            new TextRun("baz qux", 2, 2, 10, 10),
            new TextRun("more foo here", 3, 3, 10, 10),
            new TextRun("trailing foo", 4, 4, 10, 10));

        int count = await _sut.RedactMatchesAsync(doc, SrcPath, DstPath, "foo",
            new FindAndRedactOptions(), default);

        count.Should().Be(3);
        await _redact.Received(1).RedactAsync(
            SrcPath, DstPath,
            Arg.Is<IReadOnlyList<RedactionRegion>>(rs =>
                rs.Count == 3 &&
                rs[0].PageIndex == 0 && rs[0].Rect.X == 1 &&
                rs[1].PageIndex == 0 && rs[1].Rect.X == 3 &&
                rs[2].PageIndex == 0 && rs[2].Rect.X == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ZeroMatches_DoesNotCallRedactionService_ReturnsZero()
    {
        var doc = MakeDoc(new TextRun("nothing here", 0, 0, 10, 10));

        int count = await _sut.RedactMatchesAsync(doc, SrcPath, DstPath, "zzz",
            new FindAndRedactOptions(), default);

        count.Should().Be(0);
        await _redact.DidNotReceiveWithAnyArgs().RedactAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Regex_True_InterpretsQueryAsRegex_CaseInsensitiveByDefault()
    {
        // \d{3}-\d{2}-\d{4} matches SSN-like patterns (case-insensitive irrelevant for digits).
        var doc = MakeDoc(
            new TextRun("ssn 123-45-6789 here", 1, 1, 10, 10),
            new TextRun("another 987-65-4321 row", 2, 2, 10, 10),
            new TextRun("no number here", 3, 3, 10, 10));

        int count = await _sut.RedactMatchesAsync(doc, SrcPath, DstPath,
            @"\d{3}-\d{2}-\d{4}", new FindAndRedactOptions(Regex: true), default);

        count.Should().Be(2);
        await _redact.Received(1).RedactAsync(SrcPath, DstPath,
            Arg.Is<IReadOnlyList<RedactionRegion>>(rs => rs.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regex_True_CaseSensitiveOption_Honored()
    {
        // [A-Z]{3} matches uppercase only when case-sensitive; should miss "abc"/"Abc"/"aBC".
        var doc = MakeDoc(new TextRun("ABC then abc then AbC", 1, 1, 10, 10));

        int csCount = await _sut.RedactMatchesAsync(doc, SrcPath, DstPath,
            "[A-Z]{3}", new FindAndRedactOptions(CaseSensitive: true, Regex: true), default);

        csCount.Should().Be(1, "только 'ABC' все-upper, при IgnoreCase OFF");
    }

    [Fact]
    public async Task WholeWord_True_LimitsToWordBoundaries()
    {
        // "cat" inside "category"/"catalog" — must NOT match; ", cat." must match.
        var doc = MakeDoc(new TextRun("the category and catalog show a cat.", 1, 1, 10, 10));

        int count = await _sut.RedactMatchesAsync(doc, SrcPath, DstPath, "cat",
            new FindAndRedactOptions(WholeWord: true), default);

        count.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NullOrEmptyQuery_Throws(string? query)
    {
        var doc = MakeDoc(new TextRun("foo", 0, 0, 1, 1));

        var act = async () => await _sut.RedactMatchesAsync(doc, SrcPath, DstPath, query!,
            new FindAndRedactOptions(), default);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(query));
    }

    [Fact]
    public async Task BlankPaths_Throw()
    {
        var doc = MakeDoc(new TextRun("foo", 0, 0, 1, 1));

        var actSrc = async () => await _sut.RedactMatchesAsync(doc, "", DstPath, "foo",
            new FindAndRedactOptions(), default);
        var actDst = async () => await _sut.RedactMatchesAsync(doc, SrcPath, "", "foo",
            new FindAndRedactOptions(), default);

        await actSrc.Should().ThrowAsync<ArgumentException>();
        await actDst.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Substring_MatchesPreserveDocumentOrder_AndCorrectPageIndex()
    {
        // Multi-page: page 0 has 1 match, page 2 has 2 matches.
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(3);
        doc.GetTextLayerAsync(0, Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<TextLayer?>(new TextLayer(0, [new TextRun("alpha needle gamma", 10, 10, 20, 5)])));
        doc.GetTextLayerAsync(1, Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<TextLayer?>(new TextLayer(1, [new TextRun("nothing here", 0, 0, 1, 1)])));
        doc.GetTextLayerAsync(2, Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<TextLayer?>(new TextLayer(2, [
               new TextRun("first needle", 5, 5, 5, 5),
               new TextRun("second needle row", 6, 6, 6, 6)])));

        int count = await _sut.RedactMatchesAsync(doc, SrcPath, DstPath, "needle",
            new FindAndRedactOptions(), default);

        count.Should().Be(3);
        await _redact.Received(1).RedactAsync(SrcPath, DstPath,
            Arg.Is<IReadOnlyList<RedactionRegion>>(rs =>
                rs.Count == 3 &&
                rs[0].PageIndex == 0 &&
                rs[1].PageIndex == 2 &&
                rs[2].PageIndex == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regex_InvalidPattern_Throws_WithoutCallingRedaction()
    {
        var doc = MakeDoc(new TextRun("anything", 0, 0, 1, 1));

        var act = async () => await _sut.RedactMatchesAsync(doc, SrcPath, DstPath,
            "(unclosed", new FindAndRedactOptions(Regex: true), default);

        await act.Should().ThrowAsync<ArgumentException>();
        await _redact.DidNotReceiveWithAnyArgs().RedactAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task NullDocument_Throws()
    {
        var act = async () => await _sut.RedactMatchesAsync(null!, SrcPath, DstPath, "x",
            new FindAndRedactOptions(), default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var doc = MakeDoc(new TextRun("foo", 0, 0, 1, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.RedactMatchesAsync(doc, SrcPath, DstPath, "foo",
            new FindAndRedactOptions(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IDocument MakeDoc(params TextRun[] runsOnPage0)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        doc.GetTextLayerAsync(0, Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<TextLayer?>(new TextLayer(0, runsOnPage0)));
        return doc;
    }
}
