using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Foliant.Engines.Pdf;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Hermetic PAdES-B tests: each case mints a fresh self-signed certificate and builds a
/// minimally-valid signed PDF in-process (no external assets, no PDFium), so the suite is
/// deterministic and runnable on Linux CI.
/// </summary>
public sealed class PadesValidatorTests
{
    [Fact]
    public void Validate_CorrectSignatureWithTrustedRoot_AllGreen()
    {
        using X509Certificate2 cert = MakeCert("CN=Foliant PAdES Test");
        byte[] pdf = SignedPdfBuilder.Build(cert);

        var result = PadesValidator.Validate(pdf, extraTrustAnchor: cert);

        result.IsValid.Should().BeTrue();
        result.CertificateTrusted.Should().BeTrue();
        result.DocumentUntouchedSinceSigning.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Validate_BytesAppendedAfterSigning_DocumentTouched()
    {
        using X509Certificate2 cert = MakeCert("CN=Foliant PAdES Test");
        byte[] pdf = SignedPdfBuilder.Build(cert);
        byte[] tampered = [.. pdf, (byte)'\n', (byte)'%', (byte)'X']; // simulated incremental update

        var result = PadesValidator.Validate(tampered, extraTrustAnchor: cert);

        // The CMS digest still matches (appended bytes are outside /ByteRange), but the document
        // is no longer fully covered by the signature.
        result.IsValid.Should().BeTrue();
        result.DocumentUntouchedSinceSigning.Should().BeFalse();
        result.FailureReason.Should().Contain("appended");
    }

    [Fact]
    public void Validate_ContentsTamperedInsideByteRange_SignatureInvalid()
    {
        using X509Certificate2 cert = MakeCert("CN=Foliant PAdES Test");
        byte[] pdf = SignedPdfBuilder.Build(cert);

        // Flip a byte inside the first signed range (the visible page text), which the digest covers.
        int flipAt = FindSignedTextOffset(pdf);
        pdf[flipAt] ^= 0xFF;

        var result = PadesValidator.Validate(pdf, extraTrustAnchor: cert);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("does not match");
    }

    [Fact]
    public void Validate_SignerNotChainingToTrustedRoot_CryptoValidButUntrusted()
    {
        using X509Certificate2 cert = MakeCert("CN=Foliant Untrusted Signer");
        byte[] pdf = SignedPdfBuilder.Build(cert);

        // No trust anchor injected → self-signed cert cannot chain to a machine-trusted root.
        var result = PadesValidator.Validate(pdf, extraTrustAnchor: null);

        result.IsValid.Should().BeTrue();
        result.CertificateTrusted.Should().BeFalse();
        result.FailureReason.Should().Contain("chain");
    }

    [Fact]
    public void Validate_ExpiredCertificate_ReflectedAsUntrusted()
    {
        using X509Certificate2 cert = MakeCert(
            "CN=Foliant Expired Signer",
            notBefore: DateTimeOffset.UtcNow.AddYears(-2),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        byte[] pdf = SignedPdfBuilder.Build(cert);

        var result = PadesValidator.Validate(pdf, extraTrustAnchor: cert);

        // Signature math is fine, but an expired cert must not be reported as trusted.
        result.IsValid.Should().BeTrue();
        result.CertificateTrusted.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_NoSignaturePresent_FailsGracefully()
    {
        byte[] notASignedPdf = "%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF"u8.ToArray();

        var result = PadesValidator.Validate(notASignedPdf);

        result.IsValid.Should().BeFalse();
        result.CertificateTrusted.Should().BeFalse();
        result.DocumentUntouchedSinceSigning.Should().BeFalse();
        result.FailureReason.Should().Contain("No parsable signature");
    }

    [Fact]
    public void Validate_NullBytes_Throws()
    {
        Action act = () => PadesValidator.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static X509Certificate2 MakeCert(
        string subject,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddYears(1));
    }

    // Returns the file offset of the 'H' in the signed page-content literal "(Hello)" so a test
    // can corrupt a byte that the signature digest actually covers.
    private static int FindSignedTextOffset(byte[] pdf)
    {
        int idx = pdf.AsSpan().IndexOf("(Hello)"u8);
        idx.Should().BeGreaterThan(0, "the builder must embed the (Hello) content stream");
        return idx + 1;
    }
}

/// <summary>
/// Builds a minimal, structurally-valid signed PDF whose <c>/ByteRange</c> covers the entire
/// file except the <c>/Contents</c> hex window, then detached-signs that range with a real
/// <see cref="SignedCms"/>. This is the inverse of <see cref="ByteRangeParser"/> and exercises
/// the validator end-to-end.
/// </summary>
internal static class SignedPdfBuilder
{
    private const int HexCapacity = 6144; // bytes of hex room for the PKCS#7 blob (3072 raw).

    private const string PlaceholderRange = "[0000000000 0000000000 0000000000 0000000000]";

    public static byte[] Build(X509Certificate2 signerCert)
    {
        ArgumentNullException.ThrowIfNull(signerCert);

        // Step 1: lay out the file with placeholders (/ByteRange fixed-width, /Contents zero-filled).
        byte[] bytes = Encoding.ASCII.GetBytes(BuildBody());

        // Step 2: compute the real /ByteRange from the angle-bracket positions. Match the literal
        // "/Contents <" so the page's "/Contents 4 0 R" reference is not picked up by mistake.
        int sigContents = IndexOf(bytes, "/Contents <"u8, 0);
        int posLt = IndexOf(bytes, (byte)'<', sigContents);
        int posGt = IndexOf(bytes, (byte)'>', posLt + 1);
        long a = 0;
        long b = posLt + 1;          // include '<' in the first signed range
        long c = posGt;              // '>' starts the second signed range
        long d = bytes.Length - posGt;

        // Step 3: back-patch /ByteRange BEFORE signing — its bytes live inside the first signed
        // range, so writing them afterwards would invalidate the digest.
        WriteAscii(bytes, IndexOf(bytes, "[0000000000"u8, 0), FormatRange(a, b, c, d, PlaceholderRange.Length));

        // Step 4: detached-sign [a, a+b) ++ [c, c+d) — everything except the /Contents hex hole.
        byte[] signed = new byte[b + d];
        Array.Copy(bytes, (int)a, signed, 0, (int)b);
        Array.Copy(bytes, (int)c, signed, (int)b, (int)d);
        byte[] pkcs7 = Sign(signed, signerCert);

        // Step 5: splice the signature hex into the reserved hole (outside /ByteRange, so safe).
        string hex = Convert.ToHexString(pkcs7);
        if (hex.Length > HexCapacity)
        {
            throw new InvalidOperationException("PKCS#7 blob exceeds reserved /Contents capacity.");
        }
        WriteAscii(bytes, posLt + 1, hex.PadRight(HexCapacity, '0'));
        return bytes;
    }

    // A minimal multi-object PDF whose signature dictionary (obj 5) carries a fixed-width
    // /ByteRange placeholder and a HexCapacity-wide zero-filled /Contents hole.
    private static string BuildBody() =>
        "%PDF-1.7\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>endobj\n" +
        "4 0 obj<</Length 44>>stream\nBT /F1 24 Tf 20 100 Td (Hello) Tj ET\nendstream endobj\n" +
        "5 0 obj<</Type/Sig/Filter/Adobe.PPKLite/SubFilter/adbe.pkcs7.detached" +
        "/ByteRange " + PlaceholderRange +
        "/Contents <" + new string('0', HexCapacity) + ">>>endobj\n" +
        "trailer<</Root 1 0 R>>\n%%EOF";

    private static byte[] Sign(byte[] content, X509Certificate2 cert)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);
        return cms.Encode();
    }

    private static string FormatRange(long a, long b, long c, long d, int width)
    {
        string s = string.Create(CultureInfo.InvariantCulture,
            $"[{a:D10} {b:D10} {c:D10} {d:D10}]");
        // Keep identical width so no downstream offset shifts; D10 already matches the placeholder.
        return s.Length == width ? s : s.PadRight(width);
    }

    private static void WriteAscii(byte[] target, int offset, string ascii)
    {
        for (int i = 0; i < ascii.Length; i++)
        {
            target[offset + i] = (byte)ascii[i];
        }
    }

    private static int IndexOf(byte[] haystack, byte needle, int from)
    {
        int rel = haystack.AsSpan(from).IndexOf(needle);
        return rel < 0 ? -1 : from + rel;
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int from)
    {
        int rel = haystack.AsSpan(from).IndexOf(needle);
        return rel < 0 ? -1 : from + rel;
    }
}
