using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class HexColorTests
{
    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    [InlineData("#0000FF", 0, 0, 255)]
    [InlineData("FF0000", 255, 0, 0)]            // без ведущего #
    [InlineData("#FFEB3B", 255, 235, 59)]
    [InlineData("  #ffeb3b  ", 255, 235, 59)]    // пробелы + нижний регистр
    public void TryParse_SixDigit_ParsesChannels(string hex, byte r, byte g, byte b)
    {
        HexColor.TryParse(hex, out byte pr, out byte pg, out byte pb).Should().BeTrue();
        (pr, pg, pb).Should().Be((r, g, b));
    }

    [Theory]
    [InlineData("#FFF", 255, 255, 255)]
    [InlineData("#F00", 255, 0, 0)]
    [InlineData("0F0", 0, 255, 0)]
    public void TryParse_ThreeDigit_Expands(string hex, byte r, byte g, byte b)
    {
        HexColor.TryParse(hex, out byte pr, out byte pg, out byte pb).Should().BeTrue();
        (pr, pg, pb).Should().Be((r, g, b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("notacolor")]
    public void TryParse_Invalid_ReturnsFalseAndZeros(string? hex)
    {
        HexColor.TryParse(hex, out byte r, out byte g, out byte b).Should().BeFalse();
        (r, g, b).Should().Be(((byte)0, (byte)0, (byte)0));
    }
}
