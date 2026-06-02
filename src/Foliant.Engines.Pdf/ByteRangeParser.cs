using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.Globalization;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Parses the byte-level structures of a PDF signature dictionary straight from raw file
/// bytes: the <c>/ByteRange [a b c d]</c> integer quad and the <c>/Contents &lt;hex&gt;</c>
/// PKCS#7 blob. Working on raw bytes (not via PDFium) is mandatory for PAdES-B because the
/// signed-bytes digest is defined over exact file offsets — any re-serialization would shift
/// them and break verification.
/// </summary>
public static class ByteRangeParser
{
    /// <summary>
    /// Locates the first signature in <paramref name="pdf"/> and returns its parsed
    /// <c>/ByteRange</c> + decoded <c>/Contents</c>. Returns <c>null</c> when no well-formed
    /// signature dictionary is present (no <c>/Contents</c> hex window preceded by a
    /// <c>/ByteRange</c> quad).
    /// </summary>
    public static PdfSignatureBytes? Parse(ReadOnlySpan<byte> pdf)
    {
        int contentsHexStart = FindContentsHexStart(pdf, out int contentsHexEnd);
        if (contentsHexStart < 0)
        {
            return null;
        }

        // The signed bytes include the angle brackets, so the hole is exactly the hex payload:
        // [first hex char, '>'). A conformant /ByteRange then has RangeLength1 == holeStart and
        // RangeOffset2 == holeEnd (with a == 0).
        int holeStart = contentsHexStart;
        int holeEnd = contentsHexEnd;

        long[]? range = FindByteRangeBefore(pdf, contentsHexStart);
        if (range is null)
        {
            return null;
        }

        byte[]? contents = TryDecodeHex(pdf.Slice(contentsHexStart, contentsHexEnd - contentsHexStart));
        if (contents is null)
        {
            return null;
        }

        return new PdfSignatureBytes(range[0], range[1], range[2], range[3], holeStart, holeEnd, contents);
    }

    /// <summary>
    /// Assembles the bytes the signature digest is computed over: <c>[a, a+b)</c> concatenated
    /// with <c>[c, c+d)</c> — i.e. the whole document minus the <c>/Contents</c> hex window.
    /// Returns <c>null</c> if the declared ranges fall outside the actual file.
    /// </summary>
    public static byte[]? AssembleSignedBytes(ReadOnlySpan<byte> pdf, PdfSignatureBytes sig)
    {
        ArgumentNullException.ThrowIfNull(sig);
        long end1 = sig.RangeOffset1 + sig.RangeLength1;
        long end2 = sig.RangeOffset2 + sig.RangeLength2;
        if (sig.RangeOffset1 < 0 || sig.RangeOffset2 < 0 || sig.RangeLength1 < 0 || sig.RangeLength2 < 0 ||
            end1 > pdf.Length || end2 > pdf.Length || end1 > sig.RangeOffset2)
        {
            return null;
        }

        byte[] signed = new byte[sig.RangeLength1 + sig.RangeLength2];
        pdf.Slice((int)sig.RangeOffset1, (int)sig.RangeLength1).CopyTo(signed);
        pdf.Slice((int)sig.RangeOffset2, (int)sig.RangeLength2).CopyTo(signed.AsSpan((int)sig.RangeLength1));
        return signed;
    }

    private static int FindContentsHexStart(ReadOnlySpan<byte> pdf, out int hexEnd)
    {
        hexEnd = -1;
        ReadOnlySpan<byte> token = "/Contents"u8;
        int from = 0;
        while (true)
        {
            int idx = IndexOf(pdf, token, from);
            if (idx < 0)
            {
                return -1;
            }

            int p = SkipWhitespace(pdf, idx + token.Length);
            if (p < pdf.Length && pdf[p] == (byte)'<')
            {
                int close = IndexOf(pdf, ">"u8, p + 1);
                if (close > p)
                {
                    hexEnd = close;
                    return p + 1;
                }
            }
            from = idx + token.Length;
        }
    }

    private static long[]? FindByteRangeBefore(ReadOnlySpan<byte> pdf, int contentsHexStart)
    {
        ReadOnlySpan<byte> token = "/ByteRange"u8;
        // The /ByteRange entry of a signature lives in the same dictionary as its /Contents,
        // so the last occurrence before the hex window is the correct one.
        int idx = LastIndexOf(pdf, token, contentsHexStart);
        if (idx < 0)
        {
            return null;
        }

        int p = SkipWhitespace(pdf, idx + token.Length);
        if (p >= pdf.Length || pdf[p] != (byte)'[')
        {
            return null;
        }

        return ParseFourIntegers(pdf, p + 1);
    }

    private static long[]? ParseFourIntegers(ReadOnlySpan<byte> pdf, int start)
    {
        long[] values = new long[4];
        int p = start;
        for (int i = 0; i < 4; i++)
        {
            p = SkipWhitespace(pdf, p);
            int digitStart = p;
            while (p < pdf.Length && pdf[p] >= (byte)'0' && pdf[p] <= (byte)'9')
            {
                p++;
            }
            if (p == digitStart)
            {
                return null;
            }
            if (!long.TryParse(AsAscii(pdf.Slice(digitStart, p - digitStart)), NumberStyles.None,
                    CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }
        return values;
    }

    private static byte[]? TryDecodeHex(ReadOnlySpan<byte> hex)
    {
        // PDF hex strings may contain interspersed whitespace; strip it before decoding.
        Span<char> chars = hex.Length <= 4096 ? stackalloc char[hex.Length] : new char[hex.Length];
        int n = 0;
        foreach (byte b in hex)
        {
            if (b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0)
            {
                continue;
            }
            chars[n++] = (char)b;
        }

        // An odd final nibble is padded with an implicit trailing zero per the PDF spec.
        ReadOnlySpan<char> trimmed = chars[..n];
        byte[] raw;
        try
        {
            raw = Convert.FromHexString(n % 2 == 0 ? trimmed : string.Concat(trimmed, "0"));
        }
        catch (FormatException)
        {
            return null;
        }

        return TrimToDer(raw);
    }

    // Signers reserve a fixed /Contents window and zero-pad the unused tail; the padding bytes
    // are not part of the PKCS#7 DER and would break SignedCms.Decode, so trim to the exact
    // length of the leading DER value.
    private static byte[] TrimToDer(byte[] raw)
    {
        try
        {
            AsnDecoder.ReadEncodedValue(raw, AsnEncodingRules.BER, out _, out _, out int consumed);
            return consumed == raw.Length ? raw : raw[..consumed];
        }
        catch (AsnContentException)
        {
            return raw;
        }
    }

    private static int SkipWhitespace(ReadOnlySpan<byte> pdf, int p)
    {
        while (p < pdf.Length && pdf[p] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0)
        {
            p++;
        }
        return p;
    }

    private static string AsAscii(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }
        return new string(chars);
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int from)
    {
        if (from < 0)
        {
            from = 0;
        }
        int rel = haystack[from..].IndexOf(needle);
        return rel < 0 ? -1 : from + rel;
    }

    private static int LastIndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int before)
    {
        int limit = Math.Min(before, haystack.Length);
        return haystack[..limit].LastIndexOf(needle);
    }
}

/// <summary>
/// Byte-level signature coordinates extracted from a PDF: the four <c>/ByteRange</c> integers,
/// the file offsets bounding the <c>/Contents</c> hex window (exclusive of the angle brackets'
/// payload), and the decoded PKCS#7 bytes.
/// </summary>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays",
    Justification = "Internal raw-byte carrier consumed directly by SignedCms.Decode; copying defeats its purpose.")]
public sealed record PdfSignatureBytes(
    long RangeOffset1,
    long RangeLength1,
    long RangeOffset2,
    long RangeLength2,
    int ContentsHoleStart,
    int ContentsHoleEnd,
    byte[] Pkcs7);
