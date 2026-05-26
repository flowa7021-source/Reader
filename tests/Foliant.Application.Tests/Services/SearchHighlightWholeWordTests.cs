using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class SearchHighlightWholeWordTests
{
    private static TextLayer Layer(params string[] lines)
    {
        var runs = new List<TextRun>();
        for (int i = 0; i < lines.Length; i++)
        {
            runs.Add(new TextRun(lines[i], X: 0, Y: i, W: 10, H: 1));
        }
        return new TextLayer(0, runs);
    }

    [Fact]
    public void WholeWord_MatchesStandaloneWord_NotSubstring()
    {
        var layer = Layer("the cat sat", "category here", "a CAT, indeed");

        var rects = SearchHighlight.MatchRects(layer, "cat", matchCase: false, matchWholeWord: true);

        // "the cat sat" and "a CAT, indeed" match; "category" must NOT.
        rects.Should().HaveCount(2);
        rects[0].Y.Should().Be(0);
        rects[1].Y.Should().Be(2);
    }

    [Fact]
    public void WithoutWholeWord_AlsoMatchesSubstring()
    {
        var layer = Layer("the cat sat", "category here");

        var rects = SearchHighlight.MatchRects(layer, "cat", matchCase: false, matchWholeWord: false);

        rects.Should().HaveCount(2);
    }

    [Fact]
    public void WholeWord_RespectsCaseSensitivity()
    {
        var layer = Layer("the cat sat", "the CAT sat");

        var rects = SearchHighlight.MatchRects(layer, "cat", matchCase: true, matchWholeWord: true);

        rects.Should().HaveCount(1);
        rects[0].Y.Should().Be(0);
    }

    [Fact]
    public void WholeWord_MatchesAtLineBoundaries()
    {
        var layer = Layer("cat", "dog cat");

        var rects = SearchHighlight.MatchRects(layer, "cat", matchCase: false, matchWholeWord: true);

        rects.Should().HaveCount(2);
    }
}
