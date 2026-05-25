using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class SnippetHighlighterTests
{
    // ───── S2/E ─────

    [Fact]
    public void Highlight_NoMatch_ReturnsSinglePlainSegment()
    {
        var result = SnippetHighlighter.Highlight("hello world", "xyz");

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("hello world");
        result[0].IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Highlight_SingleMatch_AtStart_TwoSegments()
    {
        var result = SnippetHighlighter.Highlight("foo bar baz", "foo");

        result.Should().HaveCount(2);
        result[0].Should().Be(new SnippetSegment("foo", true));
        result[1].Should().Be(new SnippetSegment(" bar baz", false));
    }

    [Fact]
    public void Highlight_SingleMatch_InMiddle_ThreeSegments()
    {
        var result = SnippetHighlighter.Highlight("foo bar baz", "bar");

        result.Should().HaveCount(3);
        result[0].Should().Be(new SnippetSegment("foo ", false));
        result[1].Should().Be(new SnippetSegment("bar", true));
        result[2].Should().Be(new SnippetSegment(" baz", false));
    }

    [Fact]
    public void Highlight_SingleMatch_AtEnd_TwoSegments()
    {
        var result = SnippetHighlighter.Highlight("foo bar baz", "baz");

        result.Should().HaveCount(2);
        result[0].Should().Be(new SnippetSegment("foo bar ", false));
        result[1].Should().Be(new SnippetSegment("baz", true));
    }

    [Fact]
    public void Highlight_MultipleMatches_AlternatesSegments()
    {
        var result = SnippetHighlighter.Highlight("aXbXcXd", "X");

        result.Select(s => s.Text).Should().Equal(["a", "X", "b", "X", "c", "X", "d"]);
        result.Select(s => s.IsMatch).Should().Equal([false, true, false, true, false, true, false]);
    }

    [Fact]
    public void Highlight_AdjacentMatches_NoEmptySegmentsBetween()
    {
        var result = SnippetHighlighter.Highlight("XX", "X");

        result.Should().HaveCount(2);
        result[0].Should().Be(new SnippetSegment("X", true));
        result[1].Should().Be(new SnippetSegment("X", true));
    }

    [Fact]
    public void Highlight_CaseInsensitive_ByDefault()
    {
        var result = SnippetHighlighter.Highlight("Foo bar FOO", "foo");

        result.Should().HaveCount(3);
        result[0].Should().Be(new SnippetSegment("Foo", true));   // оригинальный регистр сохранён
        result[1].Should().Be(new SnippetSegment(" bar ", false));
        result[2].Should().Be(new SnippetSegment("FOO", true));
    }

    [Fact]
    public void Highlight_CaseSensitive_OnlyExactMatches()
    {
        var result = SnippetHighlighter.Highlight("Foo bar FOO foo", "foo", matchCase: true);

        result.Where(s => s.IsMatch).Should().ContainSingle()
              .Which.Text.Should().Be("foo");
    }

    [Fact]
    public void Highlight_EmptySnippet_ReturnsEmpty()
    {
        var result = SnippetHighlighter.Highlight(string.Empty, "anything");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Highlight_EmptyMatch_ReturnsSingleNonMatchSegment()
    {
        var result = SnippetHighlighter.Highlight("hello", string.Empty);

        result.Should().ContainSingle();
        result[0].Should().Be(new SnippetSegment("hello", false));
    }

    [Fact]
    public void Highlight_PreservesUnicodeMatch()
    {
        var result = SnippetHighlighter.Highlight("привет мир", "мир");

        result.Should().HaveCount(2);
        result[0].Should().Be(new SnippetSegment("привет ", false));
        result[1].Should().Be(new SnippetSegment("мир", true));
    }

    [Fact]
    public void Highlight_NullSnippet_Throws()
    {
        var act = () => SnippetHighlighter.Highlight(null!, "x");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Highlight_NullMatch_Throws()
    {
        var act = () => SnippetHighlighter.Highlight("x", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
