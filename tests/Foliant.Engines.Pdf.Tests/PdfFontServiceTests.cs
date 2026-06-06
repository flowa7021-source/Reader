using System.Globalization;
using System.Text;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Integration tests for <see cref="PdfPigFontService"/>: read the font list from the real 10-page
/// asset, and verify the embedded flag against a hand-built one-page fixture referencing a standard
/// (non-embedded) Type1 font and a TrueType font with a <c>/FontFile2</c> font descriptor. Pure
/// managed PdfPig — no native runtime — so no Slow trait (mirrors <see cref="PdfPageLabelServiceTests"/>).
/// </summary>
public sealed class PdfFontServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigFontService _service = new(NullLogger<PdfPigFontService>.Instance);

    public PdfFontServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-fonts-" + Guid.NewGuid().ToString("N"));
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
    public async Task ListFonts_RealAsset_ReturnsNonEmptyList()
    {
        var fonts = await _service.ListFontsAsync(Asset, default);

        fonts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListFonts_RealAsset_EveryEntryHasNameAndSubtype()
    {
        var fonts = await _service.ListFontsAsync(Asset, default);

        fonts.Should().OnlyContain(f => !string.IsNullOrEmpty(f.Name) && !string.IsNullOrEmpty(f.Subtype));
    }

    [Fact]
    public async Task ListFonts_RealAsset_NoDuplicateNameSubtypePairs()
    {
        var fonts = await _service.ListFontsAsync(Asset, default);

        fonts.Select(f => (f.Name, f.Subtype)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ListFonts_RealAsset_IsSortedByNameOrdinal()
    {
        var fonts = await _service.ListFontsAsync(Asset, default);

        fonts.Select(f => f.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task ListFonts_EmbeddedFixture_ReportsEmbeddedFlagPerFont()
    {
        string pdfPath = Path.Combine(_tmpDir, "fonts.pdf");
        File.WriteAllBytes(pdfPath, BuildPdfWithTwoFonts());

        var fonts = await _service.ListFontsAsync(pdfPath, default);

        fonts.Should().HaveCount(2);
        fonts.Should().ContainEquivalentOf(new PdfFontInfo("Helvetica", "Type1", IsEmbedded: false));
        fonts.Should().ContainEquivalentOf(new PdfFontInfo("ABCDEF+TestFont", "TrueType", IsEmbedded: true));
    }

    [Fact]
    public async Task ListFonts_EmbeddedFixture_IsValidPdf()
    {
        // Sanity-check the hand-built fixture opens as a valid one-page PDF before asserting on it.
        byte[] bytes = BuildPdfWithTwoFonts();

        using var doc = PdfPigDocument.Open(bytes);
        doc.NumberOfPages.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListFonts_BlankPath_Throws(string blank)
    {
        var act = () => _service.ListFontsAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static byte[] BuildPdfWithTwoFonts()
    {
        // One page referencing two fonts: a standard /Type1 /Helvetica (no descriptor → not embedded)
        // and a /TrueType font whose /FontDescriptor carries a dummy /FontFile2 stream (→ embedded).
        // The FontFile2 bytes need not be a valid font program — only the presence of the key matters.
        byte[] fontFile = Encoding.ASCII.GetBytes("dummy-font-program");
        string fontFileDict = string.Create(CultureInfo.InvariantCulture,
            $"<< /Length {fontFile.Length} /Length1 {fontFile.Length} >>");

        using var ms = new MemoryStream();
        var offsets = new long[8];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3,
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] " +
            "/Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> >>");
        WriteObject(ms, offsets, 4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        WriteObject(ms, offsets, 5,
            "<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+TestFont /FontDescriptor 6 0 R >>");
        WriteObject(ms, offsets, 6,
            "<< /Type /FontDescriptor /FontName /ABCDEF+TestFont /Flags 4 /FontFile2 7 0 R >>");
        WriteStreamObject(ms, offsets, 7, fontFileDict, fontFile);

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

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    private static string ResolveAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }
}
