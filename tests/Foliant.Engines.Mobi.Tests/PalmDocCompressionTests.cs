using System.Text;
using FluentAssertions;
using Xunit;

namespace Foliant.Engines.Mobi.Tests;

public sealed class PalmDocCompressionTests
{
    [Fact]
    public void Decompress_None_ReturnsInputVerbatim()
    {
        byte[] input = Encoding.ASCII.GetBytes("Hello, world");

        byte[] output = PalmDocCompression.Decompress(input, PalmDocCompression.CompressionNone);

        output.Should().Equal(input);
    }

    [Fact]
    public void Decompress_PalmDoc_LiteralAsciiBytes_PassThrough()
    {
        // 0x09..0x7F — литеральные ASCII-байты.
        byte[] input = Encoding.ASCII.GetBytes("ABC");

        byte[] output = PalmDocCompression.Decompress(input, PalmDocCompression.CompressionPalmDoc);

        Encoding.ASCII.GetString(output).Should().Be("ABC");
    }

    [Fact]
    public void Decompress_PalmDoc_SpacePlusLetter_ExpandsHighByte()
    {
        // 0xC0..0xFF → пробел + (byte ^ 0x80). 0xC1 → ' ' + 0x41 ('A').
        byte[] input = [0xC1];

        byte[] output = PalmDocCompression.Decompress(input, PalmDocCompression.CompressionPalmDoc);

        Encoding.ASCII.GetString(output).Should().Be(" A");
    }

    [Fact]
    public void Decompress_PalmDoc_LiteralRun_CopiesNextNBytes()
    {
        // 0x03 → скопировать следующие 3 байта буквально.
        byte[] input = [0x03, (byte)'X', (byte)'Y', (byte)'Z'];

        byte[] output = PalmDocCompression.Decompress(input, PalmDocCompression.CompressionPalmDoc);

        Encoding.ASCII.GetString(output).Should().Be("XYZ");
    }

    [Fact]
    public void Decompress_PalmDoc_BackReference_RepeatsEarlierBytes()
    {
        // "ab" затем back-reference distance=2, length=4 → копирует 4 байта назад на 2:
        // a,b → +a (idx0) → +b (idx1) → +a (idx2) → +b (idx3) = "ababab".
        // Первый байт LZ77-маркера должен быть в 0x80..0xBF, поэтому value | 0x8000.
        int distance = 2;
        int length = 4;
        int value = 0x8000 | (distance << 3) | (length - 3);
        byte hi = (byte)(value >> 8);
        byte lo = (byte)(value & 0xFF);
        byte[] input = [(byte)'a', (byte)'b', hi, lo];

        byte[] output = PalmDocCompression.Decompress(input, PalmDocCompression.CompressionPalmDoc);

        Encoding.ASCII.GetString(output).Should().Be("ababab");
    }

    [Fact]
    public void Decompress_HuffCdic_Throws()
    {
        var act = () => PalmDocCompression.Decompress([0x00], PalmDocCompression.CompressionHuffCdic);

        act.Should().Throw<NotSupportedException>();
    }
}
