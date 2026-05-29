using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class HeaderFooterSpecTests
{
    [Fact]
    public void Ctor_PreservesAllFields()
    {
        var bands = new[]
        {
            new HeaderFooterBand(HeaderFooterPosition.TopLeft, "TL"),
            new HeaderFooterBand(HeaderFooterPosition.BottomRight, "BR"),
        };
        var spec = new HeaderFooterSpec(bands, 11, 32, 64, 128);

        spec.Bands.Should().BeEquivalentTo(bands);
        spec.FontSize.Should().Be(11);
        spec.R.Should().Be(32);
        spec.G.Should().Be(64);
        spec.B.Should().Be(128);
        spec.Range.Should().BeNull();
    }

    [Fact]
    public void EmptyBands_AreAllowedAtConstructionTime()
    {
        var spec = new HeaderFooterSpec([], 10, 0, 0, 0);

        spec.Bands.Should().BeEmpty();
    }

    [Fact]
    public void FromCenterTexts_BothPresent_AddsTopCenterAndBottomCenter()
    {
        var spec = HeaderFooterSpec.FromCenterTexts("Top", "Bot", 11, 32, 64, 128);

        spec.Bands.Should().HaveCount(2);
        spec.Bands.Should().Contain(b => b.Position == HeaderFooterPosition.TopCenter && b.Text == "Top");
        spec.Bands.Should().Contain(b => b.Position == HeaderFooterPosition.BottomCenter && b.Text == "Bot");
    }

    [Fact]
    public void FromCenterTexts_NullHeader_OmitsTopBand()
    {
        var spec = HeaderFooterSpec.FromCenterTexts(null, "F", 10, 0, 0, 0);

        spec.Bands.Should().ContainSingle();
        spec.Bands[0].Position.Should().Be(HeaderFooterPosition.BottomCenter);
    }

    [Fact]
    public void FromCenterTexts_NullBothTexts_EmptyBands()
    {
        var spec = HeaderFooterSpec.FromCenterTexts(null, null, 10, 0, 0, 0);

        spec.Bands.Should().BeEmpty();
    }

    [Fact]
    public void FromCenterTexts_WhitespaceTexts_OmitsThem()
    {
        var spec = HeaderFooterSpec.FromCenterTexts("   ", "\t\n", 10, 0, 0, 0);

        spec.Bands.Should().BeEmpty();
    }

    [Fact]
    public void FromCenterTexts_PassesRangeThrough()
    {
        var range = PageRange.Parse("1-3");
        var spec = HeaderFooterSpec.FromCenterTexts("H", "F", 10, 0, 0, 0, range);

        spec.Range.Should().BeSameAs(range);
    }

    [Theory]
    [InlineData(HeaderFooterPosition.TopLeft)]
    [InlineData(HeaderFooterPosition.TopCenter)]
    [InlineData(HeaderFooterPosition.TopRight)]
    [InlineData(HeaderFooterPosition.BottomLeft)]
    [InlineData(HeaderFooterPosition.BottomCenter)]
    [InlineData(HeaderFooterPosition.BottomRight)]
    public void Band_AllSixPositionsAreSupported(HeaderFooterPosition position)
    {
        var band = new HeaderFooterBand(position, "x");
        band.Position.Should().Be(position);
    }
}
