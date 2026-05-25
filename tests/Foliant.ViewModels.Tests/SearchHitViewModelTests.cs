using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class SearchHitViewModelTests
{
    private static SearchHit MakeHit(int pageIndex = 2, string snippet = "hello world", double rank = 0.9)
        => new("fp1", "/docs/book.pdf", pageIndex, snippet, rank);

    // ───── S2/F ─────

    [Fact]
    public void Constructor_ExposesHitProperties()
    {
        var hit = MakeHit(pageIndex: 4, snippet: "foo bar", rank: 0.75);

        var vm = new SearchHitViewModel(hit, "foo");

        vm.Hit.Should().Be(hit);
        vm.PageNumber.Should().Be(5);   // 1-based
        vm.Rank.Should().Be(0.75);
        vm.DocPath.Should().Be("/docs/book.pdf");
        vm.FileName.Should().Be("book.pdf");
    }

    [Fact]
    public void SnippetSegments_MatchQuery_ReturnsHighlightedSegment()
    {
        var hit = MakeHit(snippet: "hello world");

        var vm = new SearchHitViewModel(hit, "world");

        var matchSegments = vm.SnippetSegments.Where(s => s.IsMatch).ToList();
        matchSegments.Should().ContainSingle().Which.Text.Should().Be("world");
    }

    [Fact]
    public void SnippetSegments_NoMatch_ReturnsSinglePlainSegment()
    {
        var hit = MakeHit(snippet: "hello world");

        var vm = new SearchHitViewModel(hit, "xyz");

        vm.SnippetSegments.Should().ContainSingle();
        vm.SnippetSegments[0].IsMatch.Should().BeFalse();
        vm.SnippetSegments[0].Text.Should().Be("hello world");
    }

    [Fact]
    public void SnippetSegments_CaseInsensitiveByDefault()
    {
        var hit = MakeHit(snippet: "Foo bar FOO");

        var vm = new SearchHitViewModel(hit, "foo");

        vm.SnippetSegments.Count(s => s.IsMatch).Should().Be(2);
    }

    [Fact]
    public void SnippetSegments_CaseSensitive_OnlyExactMatch()
    {
        var hit = MakeHit(snippet: "Foo bar foo");

        var vm = new SearchHitViewModel(hit, "foo", matchCase: true);

        vm.SnippetSegments.Count(s => s.IsMatch).Should().Be(1);
        vm.SnippetSegments.Single(s => s.IsMatch).Text.Should().Be("foo");
    }

    [Fact]
    public void SnippetSegments_EmptySnippet_IsEmpty()
    {
        var hit = MakeHit(snippet: string.Empty);

        var vm = new SearchHitViewModel(hit, "anything");

        vm.SnippetSegments.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullHit_Throws()
    {
        var act = () => new SearchHitViewModel(null!, "query");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullQuery_Throws()
    {
        var hit = MakeHit();

        var act = () => new SearchHitViewModel(hit, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PageNumber_IsOneBased()
    {
        var hit = MakeHit(pageIndex: 0);

        var vm = new SearchHitViewModel(hit, "x");

        vm.PageNumber.Should().Be(1);
    }

    [Fact]
    public void FileName_ExtractsLastSegmentFromPath()
    {
        var hit = new SearchHit("fp", "/a/b/c/document.pdf", 0, "text", 1.0);

        var vm = new SearchHitViewModel(hit, "text");

        vm.FileName.Should().Be("document.pdf");
    }
}
