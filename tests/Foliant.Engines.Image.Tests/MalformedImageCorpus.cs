using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.Engines.Image.Tests;

/// <summary>
/// A hand-built corpus of <b>malformed / hostile</b> image payloads used to prove the image opener's
/// robustness contract: a 0-byte, garbage, foreign-magic, header-valid-but-truncated or
/// content-renamed file must complete <i>promptly</i> — either decoding into a usable one-page document
/// or throwing a <b>tame, catchable</b> exception (a <c>SixLabors.ImageSharp</c> format exception) —
/// never a <see cref="StackOverflowException"/>, hang, OOM or process crash. Mirrors
/// <c>Foliant.Engines.Pdf.Tests.MalformedPdfCorpus</c> for the raster-image codecs.
///
/// <para>A couple of tiny valid images are encoded with ImageSharp as controls and then corrupted; the
/// rest are non-image byte patterns. Each is surfaced by a stable, human-readable name through
/// <c>[MemberData]</c>.</para>
/// </summary>
internal static class MalformedImageCorpus
{
    /// <summary>Enumerates the full corpus as <c>(Name, Bytes)</c> pairs.</summary>
    /// <returns>Every malformed (and one valid control) image payload in the corpus.</returns>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("zero-byte", []);
        yield return ("garbage", Garbage());
        yield return ("text-file-renamed", Encoding.UTF8.GetBytes("this is plain text, certainly not an image"));
        yield return ("foreign-magic-pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj <</Type/Catalog>> endobj"));
        yield return ("png-header-truncated-body", PngTruncated());
        yield return ("png-header-then-garbage", PngHeaderThenGarbage());
        yield return ("jpeg-header-truncated-body", JpegTruncated());
        yield return ("control-valid-png", ValidPng());
        yield return ("control-valid-jpeg", ValidJpeg());
    }

    // A few KB of deterministic non-zero filler that matches no image magic.
    private static byte[] Garbage()
    {
        var bytes = new byte[4096];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(((i * 41) + 13) & 0x3F | 0x20); // 0x20..0x5F: printable, no image signature.
        }

        return bytes;
    }

    // A genuine PNG signature + IHDR start, then chopped before any image data chunk.
    private static byte[] PngTruncated()
    {
        byte[] png = ValidPng();
        return png[..Math.Min(png.Length, 33)]; // 8-byte sig + part of IHDR — no IDAT.
    }

    // A valid 16-byte PNG prefix (sig + IHDR length/type) followed by junk → corrupt IHDR.
    private static byte[] PngHeaderThenGarbage()
    {
        byte[] png = ValidPng();
        var bytes = new byte[64];
        Array.Copy(png, bytes, Math.Min(16, png.Length));
        for (int i = 16; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i * 7);
        }

        return bytes;
    }

    // A valid JPEG SOI + a couple of header bytes, then chopped → bad marker.
    private static byte[] JpegTruncated()
    {
        byte[] jpg = ValidJpeg();
        return jpg[..Math.Min(jpg.Length, 20)];
    }

    /// <summary>Encodes a tiny solid PNG (the valid control + the source the corrupt PNG fixtures truncate).</summary>
    internal static byte[] ValidPng()
    {
        using var image = new Image<Rgba32>(8, 6, new Rgba32(10, 20, 200, 255));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    /// <summary>Encodes a tiny solid JPEG (the valid control + the source the corrupt JPEG fixture truncates).</summary>
    internal static byte[] ValidJpeg()
    {
        using var image = new Image<Rgba32>(8, 6, new Rgba32(10, 20, 200, 255));
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }
}
