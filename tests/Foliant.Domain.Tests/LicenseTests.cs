using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class LicenseTests
{
    private static License Make(DateTimeOffset expiresAt, params string[] features) =>
        new("alice", "PRO", expiresAt, features);

    [Fact]
    public void Constructor_PreservesUserSkuAndFeatures()
    {
        var expiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var license = new License("alice", "PRO", expiry, new[] { "export" });

        license.User.Should().Be("alice");
        license.Sku.Should().Be("PRO");
        license.ExpiresAt.Should().Be(expiry);
        license.Features.Should().ContainSingle().Which.Should().Be("export");
    }

    [Fact]
    public void License_EqualityIgnoresFeatureListIdentity_ButComparesByReference()
    {
        var expiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new License("alice", "PRO", expiry, new[] { "export" });
        var b = a with { User = "bob" };

        b.User.Should().Be("bob");
        b.Sku.Should().Be(a.Sku);
        b.Should().NotBe(a);
    }

    [Fact]
    public void IsExpired_IsTrue_WhenNowEqualsExpiresAt()
    {
        var expiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var license = Make(expiry);

        license.IsExpired(expiry).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_IsTrue_WhenNowIsAfterExpiresAt()
    {
        var expiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var license = Make(expiry);

        license.IsExpired(expiry.AddTicks(1)).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_IsFalse_JustBeforeExpiresAt()
    {
        var expiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var license = Make(expiry);

        license.IsExpired(expiry.AddTicks(-1)).Should().BeFalse();
    }

    [Theory]
    [InlineData("annotations", true)]
    [InlineData("ANNOTATIONS", true)]
    [InlineData("Annotations", true)]
    [InlineData("export", true)]
    [InlineData("missing", false)]
    public void HasFeature_IsCaseInsensitive(string code, bool expected)
    {
        var license = Make(DateTimeOffset.MaxValue, "annotations", "export");

        license.HasFeature(code).Should().Be(expected);
    }

    [Fact]
    public void HasFeature_IsFalse_WhenFeaturesEmpty()
    {
        var license = Make(DateTimeOffset.MaxValue);

        license.HasFeature("anything").Should().BeFalse();
    }

    [Fact]
    public void HasFeature_EmptyString_IsFalse_WhenNotPresent()
    {
        var license = Make(DateTimeOffset.MaxValue, "export");

        license.HasFeature(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void HasFeature_Throws_OnNull()
    {
        var license = Make(DateTimeOffset.MaxValue, "export");

        var act = () => license.HasFeature(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Valid_FactoryProducesValidStatus_WithNoReason()
    {
        var license = Make(DateTimeOffset.MaxValue, "export");

        var result = LicenseValidationResult.Valid(license);

        result.Status.Should().Be(LicenseStatus.Valid);
        result.License.Should().BeSameAs(license);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Expired_FactoryProducesExpiredStatus_WithExpiryReason()
    {
        var expiry = new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var license = Make(expiry, "export");

        var result = LicenseValidationResult.Expired(license);

        result.Status.Should().Be(LicenseStatus.Expired);
        result.License.Should().BeSameAs(license);
        result.Reason.Should().Contain(expiry.ToString("O"));
    }

    [Fact]
    public void Invalid_FactoryHasNoLicense_AndKeepsReason()
    {
        var result = LicenseValidationResult.Invalid("bad signature");

        result.Status.Should().Be(LicenseStatus.Invalid);
        result.License.Should().BeNull();
        result.Reason.Should().Be("bad signature");
    }

    [Fact]
    public void Missing_FactoryHasNoLicense_AndIsSingletonLike()
    {
        var result = LicenseValidationResult.Missing;

        result.Status.Should().Be(LicenseStatus.Missing);
        result.License.Should().BeNull();
        result.Reason.Should().Be("No license file present");
        LicenseValidationResult.Missing.Should().Be(result);
    }

    [Fact]
    public void Valid_Throws_OnNullLicense()
    {
        var act = () => LicenseValidationResult.Valid(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Expired_Throws_OnNullLicense()
    {
        var act = () => LicenseValidationResult.Expired(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LicenseBlob_EqualityIsValueBased()
    {
        var a = new LicenseBlob("{json}", "sig==");
        var b = new LicenseBlob("{json}", "sig==");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
