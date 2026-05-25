using FluentAssertions;
using Foliant.Domain;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class LicenseStatusViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static License MakeLicense(int daysUntilExpiry = 180) =>
        new("alice", "Pro", Now.AddDays(daysUntilExpiry), ["editor"]);

    [Fact]
    public void Null_Treated_As_Missing()
    {
        var vm = new LicenseStatusViewModel(null, Now);

        vm.HasResult.Should().BeFalse();
        vm.IsMissing.Should().BeTrue();
        vm.IsValid.Should().BeFalse();
        vm.User.Should().Be(string.Empty);
        vm.DisplayText.Should().Be("No license");
    }

    [Fact]
    public void Missing_Factory_IsMissing()
    {
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Missing, Now);

        vm.HasResult.Should().BeTrue();
        vm.IsMissing.Should().BeTrue();
        vm.IsLicensed.Should().BeFalse();
        vm.DaysUntilExpiry.Should().BeNull();
        vm.DisplayText.Should().Be("No license");
    }

    [Fact]
    public void Valid_Exposes_UserSkuAndExpiry()
    {
        var lic = MakeLicense(180);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.IsValid.Should().BeTrue();
        vm.IsLicensed.Should().BeTrue();
        vm.User.Should().Be("alice");
        vm.Sku.Should().Be("Pro");
        vm.ExpiresAt.Should().Be(lic.ExpiresAt);
        vm.DaysUntilExpiry.Should().Be(180);
        vm.DisplayText.Should().Be("Pro — alice (180 d left)");
    }

    [Fact]
    public void Valid_NearExpiry_DaysUntilExpiry_FloorsCorrectly()
    {
        var lic = new License("u", "Pro", Now.AddHours(36), []);  // 1.5 days
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.DaysUntilExpiry.Should().Be(1);
    }

    [Fact]
    public void Expired_HasReasonAndSku()
    {
        var lic = MakeLicense(-10);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Expired(lic), Now);

        vm.IsExpired.Should().BeTrue();
        vm.IsLicensed.Should().BeFalse();
        vm.Sku.Should().Be("Pro");
        vm.Reason.Should().Contain("expired");
        vm.DisplayText.Should().Be("Pro expired");
        vm.DaysUntilExpiry.Should().Be(-10);
    }

    [Fact]
    public void Invalid_DisplayText_IncludesReason()
    {
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Invalid("bad signature"), Now);

        vm.IsInvalid.Should().BeTrue();
        vm.IsLicensed.Should().BeFalse();
        vm.User.Should().Be(string.Empty);
        vm.DisplayText.Should().Be("Invalid: bad signature");
    }

    // ───── IsExpiringSoon (S13/H) ─────

    [Fact]
    public void IsExpiringSoon_FarFromExpiry_False()
    {
        var lic = MakeLicense(180);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.IsExpiringSoon.Should().BeFalse();
    }

    [Fact]
    public void IsExpiringSoon_WithinDefaultThreshold_True()
    {
        var lic = MakeLicense(15);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.IsExpiringSoon.Should().BeTrue();
    }

    [Fact]
    public void IsExpiringSoon_AtBoundary_True()
    {
        var lic = MakeLicense(30);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.IsExpiringSoon.Should().BeTrue();
    }

    [Fact]
    public void IsExpiringSoon_OneDayPastBoundary_False()
    {
        var lic = MakeLicense(31);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.IsExpiringSoon.Should().BeFalse();
    }

    [Fact]
    public void IsExpiringSoon_ExpiredLicense_False()
    {
        var lic = MakeLicense(-1);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Expired(lic), Now);

        vm.IsExpiringSoon.Should().BeFalse();
    }

    [Fact]
    public void IsExpiringSoon_Missing_False()
    {
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Missing, Now);

        vm.IsExpiringSoon.Should().BeFalse();
    }

    [Fact]
    public void IsExpiringSoon_CustomThreshold_AppliesTo7Days()
    {
        var lic = MakeLicense(10);
        var withDefault = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);
        var withSeven = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now, expiringSoonDays: 7);

        withDefault.IsExpiringSoon.Should().BeTrue();
        withSeven.IsExpiringSoon.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NegativeExpiringSoonDays_Throws()
    {
        var act = () => new LicenseStatusViewModel(LicenseValidationResult.Missing, Now, expiringSoonDays: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ───── HasFeature (S13/I) ─────

    [Fact]
    public void HasFeature_Valid_License_DelegatesToDomain()
    {
        var lic = new License("u", "Pro", Now.AddYears(1), ["editor", "OCR"]);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        vm.HasFeature("editor").Should().BeTrue();
        vm.HasFeature("EDITOR").Should().BeTrue();      // case-insensitive
        vm.HasFeature("ocr").Should().BeTrue();
        vm.HasFeature("watermark").Should().BeFalse();
    }

    [Fact]
    public void HasFeature_Expired_AlwaysReturnsFalse()
    {
        var lic = new License("u", "Pro", Now.AddDays(-1), ["editor"]);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Expired(lic), Now);

        vm.HasFeature("editor").Should().BeFalse();
    }

    [Fact]
    public void HasFeature_Invalid_AlwaysReturnsFalse()
    {
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Invalid("bad"), Now);

        vm.HasFeature("anything").Should().BeFalse();
    }

    [Fact]
    public void HasFeature_Missing_AlwaysReturnsFalse()
    {
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Missing, Now);

        vm.HasFeature("editor").Should().BeFalse();
    }

    [Fact]
    public void HasFeature_NullArg_Throws()
    {
        var lic = new License("u", "Pro", Now.AddYears(1), ["editor"]);
        var vm = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);

        var act = () => vm.HasFeature(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Status_FlagsAreMutuallyExclusive()
    {
        var lic = MakeLicense(30);
        var valid = new LicenseStatusViewModel(LicenseValidationResult.Valid(lic), Now);
        var expired = new LicenseStatusViewModel(LicenseValidationResult.Expired(lic), Now);
        var invalid = new LicenseStatusViewModel(LicenseValidationResult.Invalid("x"), Now);
        var missing = new LicenseStatusViewModel(LicenseValidationResult.Missing, Now);

        BoolsToInt(valid).Should().Be(1);     // только IsValid
        BoolsToInt(expired).Should().Be(1);   // только IsExpired
        BoolsToInt(invalid).Should().Be(1);   // только IsInvalid
        BoolsToInt(missing).Should().Be(1);   // только IsMissing

        static int BoolsToInt(LicenseStatusViewModel vm) =>
            (vm.IsValid ? 1 : 0)
            + (vm.IsExpired ? 1 : 0)
            + (vm.IsInvalid ? 1 : 0)
            + (vm.IsMissing ? 1 : 0);
    }
}
