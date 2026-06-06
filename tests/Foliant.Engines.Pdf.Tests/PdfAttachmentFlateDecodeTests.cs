using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Verifies the FlateDecode read path of <see cref="PdfPigAttachmentService"/>. Our writer always
/// embeds files uncompressed, so this test hand-builds a minimal one-page PDF whose embedded-file
/// stream is zlib-compressed (<c>/Filter /FlateDecode</c>) and asserts that listing reports the
/// decoded size and extraction inflates the original bytes. Pure managed — no Slow trait.
/// </summary>
public sealed class PdfAttachmentFlateDecodeTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigAttachmentService _service = new(NullLogger<PdfPigAttachmentService>.Instance);

    public PdfAttachmentFlateDecodeTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-attach-flate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task Extract_FlateCompressedEmbeddedFile_InflatesOriginalBytes()
    {
        byte[] payload = Encoding.UTF8.GetBytes("flate-compressed attachment payload " + new string('x', 300));
        string pdfPath = Path.Combine(_tmpDir, "flate.pdf");
        File.WriteAllBytes(pdfPath, BuildPdfWithFlateAttachment("flate.txt", payload));

        var list = await _service.ListAsync(pdfPath, default);
        list.Should().ContainSingle();
        list[0].Name.Should().Be("flate.txt");
        list[0].Size.Should().Be(payload.Length, "Size must reflect the decoded (inflated) length");

        string extracted = Path.Combine(_tmpDir, "out.bin");
        await _service.ExtractAsync(pdfPath, "flate.txt", extracted, default);

        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extracted)))
            .Should().Be(Convert.ToHexString(SHA256.HashData(payload)));
    }

    private static byte[] BuildPdfWithFlateAttachment(string fileName, byte[] payload)
    {
        byte[] compressed = Deflate(payload);
        string streamDict = string.Create(CultureInfo.InvariantCulture,
            $"<< /Type /EmbeddedFile /Filter /FlateDecode /Length {compressed.Length} /Params << /Size {payload.Length} >> >>");

        using var ms = new MemoryStream();
        var offsets = new long[8];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, "<< /Type /Catalog /Pages 2 0 R /Names 6 0 R >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        WriteObject(ms, offsets, 4, $"<< /Type /Filespec /F ({fileName}) /UF ({fileName}) /EF << /F 5 0 R >> >>");
        WriteStreamObject(ms, offsets, 5, streamDict, compressed);
        WriteObject(ms, offsets, 6, "<< /EmbeddedFiles 7 0 R >>");
        WriteObject(ms, offsets, 7, $"<< /Names [ ({fileName}) 4 0 R ] >>");

        long xref = ms.Position;
        var sb = new StringBuilder("xref\n0 8\n0000000000 65535 f \n");
        for (int i = 1; i <= 7; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        WriteAscii(ms, sb.ToString());
        return ms.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static void WriteObject(MemoryStream ms, long[] offsets, int number, string body)
    {
        offsets[number] = ms.Position;
        WriteAscii(ms, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
    }

    private static void WriteStreamObject(MemoryStream ms, long[] offsets, int number, string dict, byte[] bytes)
    {
        offsets[number] = ms.Position;
        WriteAscii(ms, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{dict}\nstream\n"));
        ms.Write(bytes, 0, bytes.Length);
        WriteAscii(ms, "\nendstream\nendobj\n");
    }

    private static void WriteAscii(MemoryStream ms, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        ms.Write(bytes, 0, bytes.Length);
    }
}
