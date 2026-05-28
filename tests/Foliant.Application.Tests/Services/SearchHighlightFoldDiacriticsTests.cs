using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class SearchHighlightFoldDiacriticsTests
{
    private static TextLayer Layer(params TextRun[] runs) => new(0, runs);

    [Fact]
    public void MatchRects_FoldOff_DoesNotMatchAccentedRun()
    {
        var layer = Layer(new TextRun("café au lait", 1, 2, 3, 4));

        var result = SearchHighlight.MatchRects(layer, "cafe", foldDiacritics: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public void MatchRects_FoldOn_MatchesAcrossAccents()
    {
        var layer = Layer(new TextRun("café au lait", 1, 2, 3, 4));

        var result = SearchHighlight.MatchRects(layer, "cafe", foldDiacritics: true);

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(1, 2, 3, 4));
    }

    [Fact]
    public void MatchRects_FoldOn_NeedleWithAccents_MatchesPlainRun()
    {
        var layer = Layer(new TextRun("plain cafe text", 5, 6, 7, 8));

        var result = SearchHighlight.MatchRects(layer, "café", foldDiacritics: true);

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(5, 6, 7, 8));
    }

    [Fact]
    public void MatchRects_FoldOn_StillRespectsMatchCase()
    {
        var layer = Layer(
            new TextRun("CAFÉ shop", 1, 1, 1, 1),
            new TextRun("café shop", 2, 2, 2, 2));

        var result = SearchHighlight.MatchRects(layer, "cafe", matchCase: true, foldDiacritics: true);

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(2, 2, 2, 2));
    }

    [Fact]
    public void MatchRects_FoldOn_StillRespectsWholeWord()
    {
        var layer = Layer(
            new TextRun("café au lait", 1, 1, 1, 1),
            new TextRun("cafétéria special", 2, 2, 2, 2));

        var result = SearchHighlight.MatchRects(layer, "cafe", matchWholeWord: true, foldDiacritics: true);

        result.Should().ContainSingle()
              .Which.Should().Be(new AnnotationRect(1, 1, 1, 1));
    }
}
