using System.Globalization;
using System.Text;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Tests for <see cref="PdfPigOutputIntentService"/> (read-only catalog <c>/OutputIntents</c> lister,
/// ISO 32000-1 §14.11.5). The shared 10-page asset has no <c>/OutputIntents</c> → the empty-list path;
/// hand-built fixtures (raw cos bytes, in the style of <see cref="LegacyDestsPdfFactory"/> /
/// <c>PdfFontServiceTests</c>) cover field parsing, the <c>/DestOutputProfile</c> ICC flag, array order,
/// indirect text-string refs, Unicode <c>/Info</c>, missing optionals, an indirect <c>/OutputIntents</c>
/// array, and skipped non-dict elements. Pure managed PdfPig — no native runtime — so no Slow trait.
/// </summary>
public sealed class PdfOutputIntentServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigOutputIntentService _service = new(NullLogger<PdfPigOutputIntentService>.Instance);

    public PdfOutputIntentServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-outputintents-" + Guid.NewGuid().ToString("N"));
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
    public async Task List_RealAssetWithoutOutputIntents_ReturnsEmpty()
    {
        var intents = await _service.ListAsync(Asset, default);

        intents.Should().BeEmpty();
    }

    [Fact]
    public async Task List_TwoIntentsFixture_ReturnsBothInArrayOrder()
    {
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var intents = await _service.ListAsync(path, default);

        intents.Should().HaveCount(2);
        intents.Select(i => i.Subtype).Should().Equal("GTS_PDFX", "GTS_PDFA1");
    }

    [Fact]
    public async Task List_FirstIntent_ParsesAllTextFields()
    {
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var first = (await _service.ListAsync(path, default))[0];

        first.Subtype.Should().Be("GTS_PDFX");
        first.OutputConditionIdentifier.Should().Be("FOGRA39");
        first.OutputCondition.Should().Be("Coated FOGRA39 (ISO 12647-2:2004)");
        first.RegistryName.Should().Be("http://www.color.org");
    }

    [Fact]
    public async Task List_FirstIntent_HasIccProfileTrue()
    {
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var first = (await _service.ListAsync(path, default))[0];

        first.HasIccProfile.Should().BeTrue("the first intent carries a /DestOutputProfile stream");
    }

    [Fact]
    public async Task List_FirstIntent_DecodesUnicodeInfo()
    {
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var first = (await _service.ListAsync(path, default))[0];

        // /Info is a UTF-16BE hex string carrying non-ASCII text (Cyrillic + em dash).
        first.Info.Should().Be("Цвет — ISO профиль");
    }

    [Fact]
    public async Task List_FirstIntent_ResolvesIndirectRegistryName()
    {
        // /RegistryName in the fixture is an indirect reference to a separate string object; the reader
        // must resolve it rather than returning null.
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var first = (await _service.ListAsync(path, default))[0];

        first.RegistryName.Should().Be("http://www.color.org");
    }

    [Fact]
    public async Task List_SecondIntent_HasIccProfileFalse()
    {
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var second = (await _service.ListAsync(path, default))[1];

        second.HasIccProfile.Should().BeFalse("the second intent has no /DestOutputProfile");
    }

    [Fact]
    public async Task List_SecondIntent_MissingOptionalFieldsAreNull()
    {
        string path = WriteFixture(BuildPdfWithTwoIntents());

        var second = (await _service.ListAsync(path, default))[1];

        second.Subtype.Should().Be("GTS_PDFA1");
        second.OutputConditionIdentifier.Should().Be("sRGB IEC61966-2.1");
        second.OutputCondition.Should().BeNull();
        second.RegistryName.Should().BeNull();
        second.Info.Should().BeNull();
    }

    [Fact]
    public async Task List_TwoIntentsFixture_IsValidPdf()
    {
        // Sanity-check the hand-built fixture opens as a valid one-page PDF before asserting on it.
        byte[] bytes = BuildPdfWithTwoIntents();

        using var doc = PdfPigDocument.Open(bytes);
        doc.NumberOfPages.Should().Be(1);
    }

    [Fact]
    public async Task List_IndirectOutputIntentsArray_IsResolved()
    {
        // /OutputIntents itself is an indirect reference to an array object (not an inline array).
        string path = WriteFixture(BuildPdfWithIndirectIntentsArray());

        var intents = await _service.ListAsync(path, default);

        intents.Should().ContainSingle();
        intents[0].Subtype.Should().Be("GTS_PDFX");
        intents[0].OutputConditionIdentifier.Should().Be("FOGRA39");
    }

    [Fact]
    public async Task List_ArrayWithNonDictElement_SkipsItGracefully()
    {
        // The array holds a stray non-dictionary element (a name) between two real intent dicts; the
        // reader must skip it and still surface both real entries in order.
        string path = WriteFixture(BuildPdfWithNonDictArrayElement());

        var intents = await _service.ListAsync(path, default);

        intents.Should().HaveCount(2);
        intents.Select(i => i.Subtype).Should().Equal("GTS_PDFX", "GTS_PDFA1");
    }

    [Fact]
    public async Task List_IntentWithoutSubtype_YieldsEmptySubtype()
    {
        string path = WriteFixture(BuildPdfWithIntentMissingSubtype());

        var intents = await _service.ListAsync(path, default);

        intents.Should().ContainSingle();
        intents[0].Subtype.Should().BeEmpty();
        intents[0].OutputConditionIdentifier.Should().Be("FOGRA39");
    }

    [Fact]
    public async Task List_EmptyOutputIntentsArray_ReturnsEmpty()
    {
        string path = WriteFixture(BuildPdfWithEmptyIntentsArray());

        var intents = await _service.ListAsync(path, default);

        intents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_BlankPath_Throws(string blank)
    {
        var act = () => _service.ListAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task List_CorruptBytes_ReturnsEmpty()
    {
        string path = Path.Combine(_tmpDir, "corrupt.pdf");
        File.WriteAllBytes(path, "this is not a valid PDF at all"u8.ToArray());

        var intents = await _service.ListAsync(path, default);

        intents.Should().BeEmpty("best-effort reading swallows corrupt input into an empty list");
    }

    [Fact]
    public async Task List_EmptyFile_ReturnsEmpty()
    {
        string path = Path.Combine(_tmpDir, "empty.pdf");
        File.WriteAllBytes(path, []);

        var intents = await _service.ListAsync(path, default);

        intents.Should().BeEmpty();
    }

    // --- Fixture builders ------------------------------------------------------------------------

    private string WriteFixture(byte[] bytes)
    {
        string path = Path.Combine(_tmpDir, "oi-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildPdfWithTwoIntents()
    {
        // Two output-intent dicts: (1) GTS_PDFX with a /DestOutputProfile ICC stream, all four text
        // fields populated (/RegistryName as an *indirect* string ref, /Info as UTF-16BE hex Unicode);
        // (2) GTS_PDFA1 with only /S and /OutputConditionIdentifier — no profile, no other optionals.
        byte[] icc = Encoding.ASCII.GetBytes("dummy-icc-profile-bytes");
        string iccDict = string.Create(CultureInfo.InvariantCulture, $"<< /N 4 /Length {icc.Length} >>");
        string infoHex = Utf16BeHex("Цвет — ISO профиль");

        using var ms = new MemoryStream();
        var offsets = new long[8];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, "<< /Type /Catalog /Pages 2 0 R /OutputIntents [5 0 R 6 0 R] >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        WriteStreamObject(ms, offsets, 4, iccDict, icc);
        WriteObject(ms, offsets, 5,
            "<< /Type /OutputIntent /S /GTS_PDFX " +
            "/OutputConditionIdentifier (FOGRA39) " +
            "/OutputCondition (Coated FOGRA39 \\(ISO 12647-2:2004\\)) " +
            "/RegistryName 7 0 R " +
            $"/Info {infoHex} " +
            "/DestOutputProfile 4 0 R >>");
        WriteObject(ms, offsets, 6,
            "<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (sRGB IEC61966-2.1) >>");
        WriteObject(ms, offsets, 7, "(http://www.color.org)");

        FinishPdf(ms, offsets, 7);
        return ms.ToArray();
    }

    private static byte[] BuildPdfWithIndirectIntentsArray()
    {
        // /OutputIntents is an indirect reference (5 0 R) to an array object; that array holds one intent.
        using var ms = new MemoryStream();
        var offsets = new long[7];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, "<< /Type /Catalog /Pages 2 0 R /OutputIntents 5 0 R >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        WriteObject(ms, offsets, 4,
            "<< /Type /OutputIntent /S /GTS_PDFX /OutputConditionIdentifier (FOGRA39) >>");
        WriteObject(ms, offsets, 5, "[4 0 R]");
        WriteObject(ms, offsets, 6, "<< /Unused true >>");

        FinishPdf(ms, offsets, 6);
        return ms.ToArray();
    }

    private static byte[] BuildPdfWithNonDictArrayElement()
    {
        // /OutputIntents = [intentA /SomeName intentB]: the bare name must be skipped, both dicts kept.
        using var ms = new MemoryStream();
        var offsets = new long[6];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1,
            "<< /Type /Catalog /Pages 2 0 R /OutputIntents [4 0 R /StrayName 5 0 R] >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        WriteObject(ms, offsets, 4, "<< /Type /OutputIntent /S /GTS_PDFX >>");
        WriteObject(ms, offsets, 5, "<< /Type /OutputIntent /S /GTS_PDFA1 >>");

        FinishPdf(ms, offsets, 5);
        return ms.ToArray();
    }

    private static byte[] BuildPdfWithIntentMissingSubtype()
    {
        // Intent dict without /S → Subtype must come back as an empty string (not a throw).
        using var ms = new MemoryStream();
        var offsets = new long[5];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, "<< /Type /Catalog /Pages 2 0 R /OutputIntents [4 0 R] >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        WriteObject(ms, offsets, 4, "<< /Type /OutputIntent /OutputConditionIdentifier (FOGRA39) >>");

        FinishPdf(ms, offsets, 4);
        return ms.ToArray();
    }

    private static byte[] BuildPdfWithEmptyIntentsArray()
    {
        using var ms = new MemoryStream();
        var offsets = new long[4];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, "<< /Type /Catalog /Pages 2 0 R /OutputIntents [] >>");
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");

        FinishPdf(ms, offsets, 3);
        return ms.ToArray();
    }

    // --- Raw cos writers (offset-array style, mirrors PdfFontServiceTests) ------------------------

    private static void FinishPdf(MemoryStream ms, long[] offsets, int lastObject)
    {
        long xref = ms.Position;
        var sb = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {lastObject + 1}\n"));
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i <= lastObject; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {lastObject + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        WriteAscii(ms, sb.ToString());
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

    private static string Utf16BeHex(string value)
    {
        // PDF text string as <hex> in UTF-16BE with a leading BOM (FE FF) — the form PdfTextString emits
        // and the reader decodes.
        var sb = new StringBuilder("<FEFF");
        foreach (byte b in Encoding.BigEndianUnicode.GetBytes(value))
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        sb.Append('>');
        return sb.ToString();
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
