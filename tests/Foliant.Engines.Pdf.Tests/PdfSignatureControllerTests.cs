using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

public sealed class PdfSignatureControllerTests
{
    // ───── MapKindFromSubFilter ─────

    [Theory]
    [InlineData("adbe.pkcs7.detached", SignatureKind.Cms)]
    [InlineData("adbe.pkcs7.sha1", SignatureKind.Cms)]
    [InlineData("adbe.x509.rsa_sha1", SignatureKind.Cms)]
    [InlineData("ETSI.CAdES.detached", SignatureKind.PadesB)]
    [InlineData("etsi.cades.detached", SignatureKind.PadesB)] // case-insensitive
    [InlineData("ETSI.RFC3161", SignatureKind.PadesT)]
    [InlineData("unknown.subfilter.value", SignatureKind.Cms)]
    public void MapKindFromSubFilter_ReturnsExpectedKind(string subFilter, SignatureKind expected)
    {
        PdfSignatureController.MapKindFromSubFilter(subFilter).Should().Be(expected);
    }

    // ───── ParsePdfDate ─────

    [Fact]
    public void ParsePdfDate_FullForm_ParsesCorrectly()
    {
        // D:20231215143000+03'00' — December 15, 2023, 14:30:00 UTC+3.
        var result = PdfSignatureController.ParsePdfDate("D:20231215143000+03'00'");
        result.Year.Should().Be(2023);
        result.Month.Should().Be(12);
        result.Day.Should().Be(15);
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(0);
        result.Offset.Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void ParsePdfDate_NegativeOffset_Works()
    {
        var result = PdfSignatureController.ParsePdfDate("D:20240601090000-05'00'");
        result.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void ParsePdfDate_ZuluTime_OffsetZero()
    {
        var result = PdfSignatureController.ParsePdfDate("D:20240101000000Z");
        result.Offset.Should().Be(TimeSpan.Zero);
        result.Year.Should().Be(2024);
    }

    [Fact]
    public void ParsePdfDate_NoPrefix_StillWorks()
    {
        var result = PdfSignatureController.ParsePdfDate("20231215143000+03'00'");
        result.Year.Should().Be(2023);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date at all")]
    [InlineData("D:abcd")]
    public void ParsePdfDate_Invalid_ReturnsMinValue(string? input)
    {
        PdfSignatureController.ParsePdfDate(input!).Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public void ParsePdfDate_PartialForm_FillsWithDefaults()
    {
        var result = PdfSignatureController.ParsePdfDate("D:2023");
        result.Year.Should().Be(2023);
        result.Month.Should().Be(1);
        result.Day.Should().Be(1);
    }

    // ───── ExtractSignerName ─────

    [Fact]
    public void ExtractSignerName_Empty_ReturnsUnknown()
    {
        PdfSignatureController.ExtractSignerName([]).Should().Be("(unknown)");
    }

    [Fact]
    public void ExtractSignerName_NotPkcs7_ReturnsUnknown()
    {
        byte[] garbage = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        PdfSignatureController.ExtractSignerName(garbage).Should().Be("(unknown)");
    }

    [Fact]
    public void ExtractSignerName_ValidPkcs7_ReturnsCommonName()
    {
        // Build a self-signed cert with a known CN, sign a small payload, extract.
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Test Signer Foliant, OU=Engineering, O=Foliant", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        var content = new ContentInfo(System.Text.Encoding.UTF8.GetBytes("payload"));
        var cms = new SignedCms(content);
        cms.ComputeSignature(new CmsSigner(cert));
        byte[] encoded = cms.Encode();

        PdfSignatureController.ExtractSignerName(encoded).Should().Be("Test Signer Foliant");
    }

    // ───── Constructor / Signatures path ─────

    [Fact]
    public void Ctor_BlankPath_Throws()
    {
        Func<PdfSignatureController> act = () => new PdfSignatureController("  ");
        act.Should().Throw<ArgumentException>();
    }

    // ───── End-to-end with an UNSIGNED PDF (Slow — uses PDFium native) ─────

    [Fact]
    [Trait("Category", "Slow")]
    public void UnsignedPdf_ReturnsEmptySignatureList()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "foliant-sig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Hand-build minimal unsigned PDF via PdfPig.
            var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
            var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
            var page = builder.AddPage(400, 400);
            page.AddText("unsigned", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 50), font);
            string path = Path.Combine(tmpDir, "unsigned.pdf");
            File.WriteAllBytes(path, builder.Build());

            var ctrl = new PdfSignatureController(path);

            ctrl.Signatures.Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ValidateAsync_HonestNotImplemented()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "foliant-sig-val-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
            var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
            builder.AddPage(200, 200).AddText("x", 10, new UglyToad.PdfPig.Core.PdfPoint(10, 10), font);
            string path = Path.Combine(tmpDir, "x.pdf");
            File.WriteAllBytes(path, builder.Build());

            var ctrl = new PdfSignatureController(path);
            var fakeSignature = new DocumentSignature("Test", DateTimeOffset.UtcNow, null, null, SignatureKind.Cms);
            var result = await ctrl.ValidateAsync(fakeSignature, CancellationToken.None);

            result.IsValid.Should().BeFalse();
            result.CertificateTrusted.Should().BeFalse();
            result.DocumentUntouchedSinceSigning.Should().BeFalse();
            result.FailureReason.Should().Contain("not implemented");
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
