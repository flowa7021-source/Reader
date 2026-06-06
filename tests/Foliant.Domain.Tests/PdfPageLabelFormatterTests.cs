using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

/// <summary>
/// Numeric-style conversion and range-selection logic for <see cref="PdfPageLabelFormatter"/>.
/// </summary>
public sealed class PdfPageLabelFormatterTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(42, "42")]
    public void FormatNumber_Arabic(int value, string expected) =>
        PdfPageLabelFormatter.FormatNumber(value, PdfPageLabelStyle.Arabic).Should().Be(expected);

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(40, "XL")]
    [InlineData(90, "XC")]
    [InlineData(2024, "MMXXIV")]
    public void FormatNumber_UpperRoman(int value, string expected) =>
        PdfPageLabelFormatter.FormatNumber(value, PdfPageLabelStyle.UpperRoman).Should().Be(expected);

    [Theory]
    [InlineData(1, "i")]
    [InlineData(3, "iii")]
    [InlineData(14, "xiv")]
    public void FormatNumber_LowerRoman(int value, string expected) =>
        PdfPageLabelFormatter.FormatNumber(value, PdfPageLabelStyle.LowerRoman).Should().Be(expected);

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "ZZ")]
    [InlineData(53, "AAA")]
    public void FormatNumber_UpperLetters(int value, string expected) =>
        PdfPageLabelFormatter.FormatNumber(value, PdfPageLabelStyle.UpperLetters).Should().Be(expected);

    [Theory]
    [InlineData(1, "a")]
    [InlineData(28, "bb")]
    public void FormatNumber_LowerLetters(int value, string expected) =>
        PdfPageLabelFormatter.FormatNumber(value, PdfPageLabelStyle.LowerLetters).Should().Be(expected);

    [Fact]
    public void FormatNumber_None_ReturnsEmpty() =>
        PdfPageLabelFormatter.FormatNumber(5, PdfPageLabelStyle.None).Should().BeEmpty();

    [Fact]
    public void FormatNumber_RomanWithValueBelowOne_Throws()
    {
        Action act = () => PdfPageLabelFormatter.FormatNumber(0, PdfPageLabelStyle.UpperRoman);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Format_SingleArabicRange_CountsFromStart()
    {
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic)];

        PdfPageLabelFormatter.Format(ranges, 0).Should().Be("1");
        PdfPageLabelFormatter.Format(ranges, 4).Should().Be("5");
    }

    [Fact]
    public void Format_HonoursStartOffset()
    {
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, start: 5)];

        PdfPageLabelFormatter.Format(ranges, 0).Should().Be("5");
        PdfPageLabelFormatter.Format(ranges, 2).Should().Be("7");
    }

    [Fact]
    public void Format_AppliesPrefix()
    {
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, "A-")];

        PdfPageLabelFormatter.Format(ranges, 2).Should().Be("A-3");
    }

    [Fact]
    public void Format_NoneStyle_PrefixOnly()
    {
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.None, "Cover")];

        PdfPageLabelFormatter.Format(ranges, 0).Should().Be("Cover");
    }

    [Fact]
    public void Format_PicksNearestRangeOnTheLeft()
    {
        // Front matter i, ii, iii (pages 0-2), then body 1, 2, 3 … from page 3.
        IReadOnlyList<PdfPageLabelRange> ranges =
        [
            PdfPageLabelRange.Create(0, PdfPageLabelStyle.LowerRoman),
            PdfPageLabelRange.Create(3, PdfPageLabelStyle.Arabic),
        ];

        PdfPageLabelFormatter.Format(ranges, 0).Should().Be("i");
        PdfPageLabelFormatter.Format(ranges, 2).Should().Be("iii");
        PdfPageLabelFormatter.Format(ranges, 3).Should().Be("1");
        PdfPageLabelFormatter.Format(ranges, 5).Should().Be("3");
    }

    [Fact]
    public void Format_NoCoveringRange_ReturnsEmpty()
    {
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(2, PdfPageLabelStyle.Arabic)];

        PdfPageLabelFormatter.Format(ranges, 0).Should().BeEmpty();
        PdfPageLabelFormatter.Format([], 0).Should().BeEmpty();
    }

    [Fact]
    public void Format_NullRanges_Throws()
    {
        Action act = () => PdfPageLabelFormatter.Format(null!, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Format_NegativePageIndex_Throws()
    {
        Action act = () => PdfPageLabelFormatter.Format([], -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
