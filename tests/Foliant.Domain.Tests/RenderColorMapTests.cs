using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class RenderColorMapTests
{
    [Fact]
    public void ApplyTheme_Original_LeavesBufferUnchanged()
    {
        byte[] bgra = [10, 20, 30, 255, 40, 50, 60, 128];
        var copy = (byte[])bgra.Clone();

        RenderColorMap.ApplyTheme(bgra, RenderTheme.Original);

        bgra.Should().Equal(copy);
    }

    [Fact]
    public void Invert_FlipsBgr_PreservesAlpha()
    {
        byte[] bgra = [10, 20, 30, 255, 0, 0, 0, 128];

        RenderColorMap.Invert(bgra);

        bgra.Should().Equal([245, 235, 225, 255, 255, 255, 255, 128]);
    }

    [Theory]
    [InlineData(0, 255)]    // black -> inverted 255 -> stretched clamps high
    [InlineData(255, 0)]    // white -> inverted 0   -> stretched clamps low
    [InlineData(128, 127)]  // mid stays ~mid
    [InlineData(100, 171)]
    public void HighContrast_StretchesChannelAroundMidpoint(byte input, byte expected)
    {
        byte[] bgra = [input, input, input, 200];

        RenderColorMap.ApplyHighContrast(bgra);

        bgra[0].Should().Be(expected);
        bgra[1].Should().Be(expected);
        bgra[2].Should().Be(expected);
        bgra[3].Should().Be(200); // alpha untouched
    }

    [Fact]
    public void ApplyTheme_Dark_MatchesInvert()
    {
        byte[] viaTheme = [12, 34, 56, 255];
        byte[] viaInvert = (byte[])viaTheme.Clone();

        RenderColorMap.ApplyTheme(viaTheme, RenderTheme.Dark);
        RenderColorMap.Invert(viaInvert);

        viaTheme.Should().Equal(viaInvert);
    }

    [Fact]
    public void ApplyTheme_HighContrast_MatchesApplyHighContrast()
    {
        byte[] viaTheme = [12, 34, 56, 255];
        byte[] viaDirect = (byte[])viaTheme.Clone();

        RenderColorMap.ApplyTheme(viaTheme, RenderTheme.HighContrast);
        RenderColorMap.ApplyHighContrast(viaDirect);

        viaTheme.Should().Equal(viaDirect);
    }

    [Fact]
    public void Invert_OnlyProcessesCompleteQuads()
    {
        byte[] bgra = [10, 20, 30, 255, 99]; // 5 bytes: one full quad + a trailing byte

        RenderColorMap.Invert(bgra);

        bgra[4].Should().Be(99); // trailing partial pixel left untouched (no OOB)
    }
}
