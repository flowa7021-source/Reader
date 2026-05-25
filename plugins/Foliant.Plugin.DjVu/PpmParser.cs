namespace Foliant.Plugin.DjVu;

/// <summary>Decoded binary PPM (P6): RGB pixels converted to BGRA32 (opaque alpha).</summary>
public readonly record struct PpmImage(int Width, int Height, int Stride, ReadOnlyMemory<byte> Bgra32);

/// <summary>
/// Pure parser for binary PPM (magic <c>P6</c>) produced by <c>ddjvu -format=ppm</c>.
/// No IO, no allocations beyond the result buffer; safe to unit test in isolation.
/// </summary>
public static class PpmParser
{
    private readonly record struct Header(int Width, int Height, int PayloadStart);

    /// <summary>
    /// Parses a P6 PPM and converts RGB → BGRA32 (Stride = Width*4, alpha = 0xFF).
    /// Throws <see cref="FormatException"/> on a malformed header, unsupported maxval,
    /// or truncated pixel payload.
    /// </summary>
    public static PpmImage Parse(ReadOnlySpan<byte> data)
    {
        Header h = ParseHeader(data);

        long rgbBytes = (long)h.Width * h.Height * 3;
        if (h.PayloadStart + rgbBytes > data.Length)
        {
            throw new FormatException(
                $"PPM: truncated payload; need {rgbBytes} bytes, have {data.Length - h.PayloadStart}.");
        }

        byte[] bgra = ConvertRgbToBgra(data.Slice(h.PayloadStart, (int)rgbBytes), h.Width, h.Height);
        return new PpmImage(h.Width, h.Height, h.Width * 4, bgra);
    }

    private static Header ParseHeader(ReadOnlySpan<byte> data)
    {
        int pos = 0;
        if (!ReadMagic(data, ref pos))
        {
            throw new FormatException("PPM: expected magic 'P6'.");
        }

        int width = ReadHeaderInt(data, ref pos, "width");
        int height = ReadHeaderInt(data, ref pos, "height");
        int maxval = ReadHeaderInt(data, ref pos, "maxval");

        if (width <= 0 || height <= 0)
        {
            throw new FormatException($"PPM: non-positive dimensions {width}x{height}.");
        }

        if (maxval != 255)
        {
            // FRAGILE: ddjvu always emits 8-bit (maxval 255); 16-bit PPM is out of scope.
            throw new FormatException($"PPM: unsupported maxval {maxval}; only 8-bit (255) is supported.");
        }

        // Exactly one whitespace byte separates the header from the binary payload.
        return new Header(width, height, pos + 1);
    }

    private static byte[] ConvertRgbToBgra(ReadOnlySpan<byte> rgb, int width, int height)
    {
        byte[] bgra = new byte[width * height * 4];
        int src = 0;
        int dst = 0;
        while (src < rgb.Length)
        {
            bgra[dst] = rgb[src + 2];     // B
            bgra[dst + 1] = rgb[src + 1]; // G
            bgra[dst + 2] = rgb[src];     // R
            bgra[dst + 3] = 0xFF;         // A (opaque)
            src += 3;
            dst += 4;
        }

        return bgra;
    }

    private static bool ReadMagic(ReadOnlySpan<byte> data, ref int pos)
    {
        if (data.Length < 2 || data[0] != (byte)'P' || data[1] != (byte)'6')
        {
            return false;
        }

        pos = 2;
        return true;
    }

    private static int ReadHeaderInt(ReadOnlySpan<byte> data, ref int pos, string field)
    {
        SkipWhitespaceAndComments(data, ref pos);

        int start = pos;
        while (pos < data.Length && IsDigit(data[pos]))
        {
            pos++;
        }

        if (pos == start)
        {
            throw new FormatException($"PPM: missing {field} in header.");
        }

        long value = 0;
        for (int i = start; i < pos; i++)
        {
            value = (value * 10) + (data[i] - '0');
            if (value > int.MaxValue)
            {
                throw new FormatException($"PPM: {field} overflows.");
            }
        }

        return (int)value;
    }

    private static void SkipWhitespaceAndComments(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length)
        {
            byte b = data[pos];
            if (b == (byte)'#')
            {
                while (pos < data.Length && data[pos] != (byte)'\n')
                {
                    pos++;
                }
            }
            else if (IsWhitespace(b))
            {
                pos++;
            }
            else
            {
                return;
            }
        }
    }

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0B or 0x0C;
}
