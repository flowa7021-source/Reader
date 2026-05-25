using System.Text;
using FluentAssertions;
using Foliant.Plugin.DjVu;
using Xunit;

namespace Foliant.Plugin.DjVu.Tests;

public sealed class PpmParserTests
{
    [Fact]
    public void Parse_TwoPixelImage_ConvertsRgbToBgra()
    {
        // 2x1 P6: pixel0 RGB=(10,20,30), pixel1 RGB=(40,50,60).
        byte[] ppm = BuildP6("2 1", [10, 20, 30, 40, 50, 60]);

        var img = PpmParser.Parse(ppm);

        img.Width.Should().Be(2);
        img.Height.Should().Be(1);
        img.Stride.Should().Be(8); // width * 4
        img.Bgra32.ToArray().Should().Equal(
            30, 20, 10, 0xFF, // pixel0: B,G,R,A
            60, 50, 40, 0xFF); // pixel1
    }

    [Fact]
    public void Parse_SkipsCommentsAndExtraWhitespace()
    {
        byte[] header = Encoding.ASCII.GetBytes("P6\n# a comment\n1   1\n255\n");
        byte[] ppm = [.. header, 1, 2, 3];

        var img = PpmParser.Parse(ppm);

        img.Width.Should().Be(1);
        img.Height.Should().Be(1);
        img.Bgra32.ToArray().Should().Equal(3, 2, 1, 0xFF);
    }

    [Fact]
    public void Parse_WrongMagic_Throws()
    {
        byte[] ppm = Encoding.ASCII.GetBytes("P3\n1 1\n255\n");

        var act = () => PpmParser.Parse(ppm);

        act.Should().Throw<FormatException>().WithMessage("*P6*");
    }

    [Fact]
    public void Parse_TruncatedPayload_Throws()
    {
        // Header promises 1x1 (3 RGB bytes) but supplies only 1.
        byte[] ppm = BuildP6("1 1", [42]);

        var act = () => PpmParser.Parse(ppm);

        act.Should().Throw<FormatException>().WithMessage("*truncated*");
    }

    [Fact]
    public void Parse_UnsupportedMaxval_Throws()
    {
        byte[] ppm = Encoding.ASCII.GetBytes("P6\n1 1\n65535\n");

        var act = () => PpmParser.Parse(ppm);

        act.Should().Throw<FormatException>().WithMessage("*maxval*");
    }

    [Theory]
    [InlineData("P6\n0 1\n255\n")]
    [InlineData("P6\n1 0\n255\n")]
    public void Parse_NonPositiveDimensions_Throws(string text)
    {
        var act = () => PpmParser.Parse(Encoding.ASCII.GetBytes(text));

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingDimension_Throws()
    {
        byte[] ppm = Encoding.ASCII.GetBytes("P6\n");

        var act = () => PpmParser.Parse(ppm);

        act.Should().Throw<FormatException>();
    }

    private static byte[] BuildP6(string dims, byte[] pixels)
    {
        byte[] header = Encoding.ASCII.GetBytes($"P6\n{dims}\n255\n");
        return [.. header, .. pixels];
    }
}
