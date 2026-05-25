using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class SearchHighlightTests
{
    private static TextLayer Layer(params TextRun[] runs) => new(0, runs);

    [Fact]
    public void MatchRects_CaseInsensitive_ByDefault()
    {
        var layer = Layer(new TextRun("Hello World", 1, 2, 3, 4));

        var result = SearchHighlight.MatchRects(layer, "hello");

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(1, 2, 3, 4));
    }

    [Fact]
    public void MatchRects_MatchCaseTrue_IsCaseSensitive()
    {
        var layer = Layer(
            new TextRun("Hello", 1, 1, 1, 1),
            new TextRun("hello", 2, 2, 2, 2));

        var result = SearchHighlight.MatchRects(layer, "hello", matchCase: true);

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(2, 2, 2, 2));
    }

    [Fact]
    public void MatchRects_NoMatch_ReturnsEmpty()
    {
        var layer = Layer(new TextRun("abc", 0, 0, 1, 1));

        var result = SearchHighlight.MatchRects(layer, "xyz");

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void MatchRects_EmptyOrWhitespaceQuery_ReturnsEmpty(string query)
    {
        var layer = Layer(new TextRun("anything", 0, 0, 1, 1));

        var result = SearchHighlight.MatchRects(layer, query);

        result.Should().BeEmpty();
    }

    [Fact]
    public void MatchRects_MultipleMatches_PreservesDocumentOrder()
    {
        var layer = Layer(
            new TextRun("foo one", 1, 1, 1, 1),
            new TextRun("bar", 2, 2, 2, 2),
            new TextRun("foo two", 3, 3, 3, 3));

        var result = SearchHighlight.MatchRects(layer, "foo");

        result.Should().Equal(
            new AnnotationRect(1, 1, 1, 1),
            new AnnotationRect(3, 3, 3, 3));
    }

    [Fact]
    public void MatchRects_EmittedRect_EqualsMatchingRunGeometry()
    {
        var layer = Layer(new TextRun("needle here", 12.5, 34.25, 56.75, 7.125));

        var result = SearchHighlight.MatchRects(layer, "needle");

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(12.5, 34.25, 56.75, 7.125));
    }

    [Fact]
    public void MatchRects_SubstringInsideLine_MatchesThatLine()
    {
        var layer = Layer(new TextRun("the quick brown fox", 5, 6, 7, 8));

        var result = SearchHighlight.MatchRects(layer, "quick");

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(5, 6, 7, 8));
    }

    [Fact]
    public void MatchRects_NullLayer_Throws()
    {
        var act = () => SearchHighlight.MatchRects(null!, "x");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MatchRects_NullQuery_Throws()
    {
        var layer = Layer(new TextRun("x", 0, 0, 1, 1));

        var act = () => SearchHighlight.MatchRects(layer, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
