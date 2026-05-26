using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class SearchHitTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 5)]
    [InlineData(99, 100)]
    public void PageNumber_IsOneBased(int pageIndex, int expectedPageNumber)
    {
        var hit = new SearchHit("fp", "/docs/a.pdf", pageIndex, "snippet", 0.5);

        hit.PageNumber.Should().Be(expectedPageNumber);
    }

    [Fact]
    public void SearchHit_PreservesConstructorFields()
    {
        var hit = new SearchHit("fp-abc", "/docs/a.pdf", 2, "the quick fox", 0.87);

        hit.DocFingerprint.Should().Be("fp-abc");
        hit.Path.Should().Be("/docs/a.pdf");
        hit.PageIndex.Should().Be(2);
        hit.Snippet.Should().Be("the quick fox");
        hit.Rank.Should().Be(0.87);
    }

    [Fact]
    public void SearchHit_EqualityIsValueBased()
    {
        var a = new SearchHit("fp", "/p", 1, "s", 0.1);
        var b = new SearchHit("fp", "/p", 1, "s", 0.1);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void SearchQuery_Defaults_AreApplied()
    {
        var query = new SearchQuery("hello");

        query.Text.Should().Be("hello");
        query.MaxResults.Should().Be(100);
        query.RestrictToDocFingerprint.Should().BeNull();
        query.MatchCase.Should().BeFalse();
        query.MatchWholeWord.Should().BeFalse();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\t", true)]
    [InlineData("hello", false)]
    [InlineData("  x  ", false)]
    public void IsEmpty_ReflectsWhitespaceOnlyText(string text, bool expectedEmpty)
    {
        new SearchQuery(text).IsEmpty.Should().Be(expectedEmpty);
    }

    [Fact]
    public void SearchQuery_CanOverrideOptionalFlags()
    {
        var query = new SearchQuery("term", MaxResults: 10, RestrictToDocFingerprint: "fp", MatchCase: true, MatchWholeWord: true);

        query.MaxResults.Should().Be(10);
        query.RestrictToDocFingerprint.Should().Be("fp");
        query.MatchCase.Should().BeTrue();
        query.MatchWholeWord.Should().BeTrue();
    }
}
