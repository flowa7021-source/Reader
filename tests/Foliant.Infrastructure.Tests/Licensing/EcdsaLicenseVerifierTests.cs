using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Licensing;
using Xunit;

namespace Foliant.Infrastructure.Tests.Licensing;

public sealed class EcdsaLicenseVerifierTests : IDisposable
{
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly EcdsaLicenseVerifier _sut;

    public EcdsaLicenseVerifierTests()
    {
        _sut = new EcdsaLicenseVerifier(_signingKey.ExportSubjectPublicKeyInfoPem());
    }

    public void Dispose()
    {
        _sut.Dispose();
        _signingKey.Dispose();
    }

    [Fact]
    public void Verify_WellFormedSignedLicense_ReturnsValid()
    {
        var (json, sig) = SignLicense(new License(
            User: "alice@example.com",
            Sku: "Pro",
            ExpiresAt: DateTimeOffset.UtcNow.AddYears(1),
            Features: ["editor", "ocr"]));

        var result = _sut.Verify(json, sig, DateTimeOffset.UtcNow);

        result.Status.Should().Be(LicenseStatus.Valid);
        result.License!.User.Should().Be("alice@example.com");
        result.License.HasFeature("OCR").Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedJson_ReturnsInvalid()
    {
        var (json, sig) = SignLicense(new License(
            "alice", "Pro", DateTimeOffset.UtcNow.AddYears(1), ["editor"]));
        // подменяем User уже после подписи
        var tampered = json.Replace("alice", "mallory", StringComparison.Ordinal);

        var result = _sut.Verify(tampered, sig, DateTimeOffset.UtcNow);

        result.Status.Should().Be(LicenseStatus.Invalid);
        result.License.Should().BeNull();
    }

    [Fact]
    public void Verify_BadSignatureBase64_ReturnsInvalid()
    {
        var json = JsonSerializer.Serialize(new License(
            "alice", "Pro", DateTimeOffset.UtcNow.AddYears(1), ["editor"]));

        var result = _sut.Verify(json, "not-base64-!!!", DateTimeOffset.UtcNow);

        result.Status.Should().Be(LicenseStatus.Invalid);
        result.Reason.Should().Contain("base64");
    }

    [Fact]
    public void Verify_SignatureFromDifferentKey_ReturnsInvalid()
    {
        var json = JsonSerializer.Serialize(new License(
            "alice", "Pro", DateTimeOffset.UtcNow.AddYears(1), ["editor"]));

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherSig = Convert.ToBase64String(otherKey.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256));

        var result = _sut.Verify(json, otherSig, DateTimeOffset.UtcNow);

        result.Status.Should().Be(LicenseStatus.Invalid);
    }

    [Fact]
    public void Verify_ExpiredLicense_ReturnsExpiredButCarriesPayload()
    {
        var pastExpiry = DateTimeOffset.UtcNow.AddDays(-1);
        var (json, sig) = SignLicense(new License(
            "alice", "Pro", pastExpiry, ["editor"]));

        var result = _sut.Verify(json, sig, DateTimeOffset.UtcNow);

        result.Status.Should().Be(LicenseStatus.Expired);
        result.License!.User.Should().Be("alice");   // payload всё равно отдаём — UI покажет "your license expired on ..."
    }

    [Fact]
    public void Verify_MalformedJson_ReturnsInvalid()
    {
        // подписываем что-то, что валидно с точки зрения подписи но не парсится как License
        var raw = "{ this is not json";
        var sig = Convert.ToBase64String(_signingKey.SignData(Encoding.UTF8.GetBytes(raw), HashAlgorithmName.SHA256));

        var result = _sut.Verify(raw, sig, DateTimeOffset.UtcNow);

        result.Status.Should().Be(LicenseStatus.Invalid);
        result.Reason.Should().Contain("malformed");
    }

    [Fact]
    public void License_HasFeature_IsCaseInsensitive()
    {
        var lic = new License("u", "Pro", DateTimeOffset.UtcNow, ["Editor", "OCR"]);

        lic.HasFeature("editor").Should().BeTrue();
        lic.HasFeature("ocr").Should().BeTrue();
        lic.HasFeature("missing").Should().BeFalse();
    }

    [Fact]
    public void License_IsExpired_IncludesBoundary()
    {
        var expiry = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var lic = new License("u", "Pro", expiry, []);

        lic.IsExpired(expiry).Should().BeTrue();
        lic.IsExpired(expiry.AddSeconds(-1)).Should().BeFalse();
    }

    private (string json, string signatureBase64) SignLicense(License license)
    {
        var json = JsonSerializer.Serialize(license);
        var sig = _signingKey.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256);
        return (json, Convert.ToBase64String(sig));
    }
}
