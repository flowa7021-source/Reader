using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

/// <summary>
/// Контрольные тесты <see cref="PdfEncryptionSpec"/> — record-семантика (равенство,
/// <c>with</c>), фабрика <c>Create</c> с инвариантами (owner ≠ пустой, user не null).
/// </summary>
public sealed class PdfEncryptionSpecTests
{
    [Fact]
    public void Create_ValidArguments_RoundTripsAllFields()
    {
        var spec = PdfEncryptionSpec.Create("u-pwd", "o-pwd", PdfPermissions.Print | PdfPermissions.Copy);

        spec.UserPassword.Should().Be("u-pwd");
        spec.OwnerPassword.Should().Be("o-pwd");
        spec.AllowedPermissions.Should().Be(PdfPermissions.Print | PdfPermissions.Copy);
    }

    [Fact]
    public void Create_EmptyUserPasswordPermittedSpecAllowsAnonymousOpen()
    {
        // Acrobat-конвенция: user="" + owner ≠ "" → документ открывается без пароля,
        // но permissions enforce-ятся как для user'а.
        var spec = PdfEncryptionSpec.Create(string.Empty, "owner-secret", PdfPermissions.None);

        spec.UserPassword.Should().BeEmpty();
        spec.OwnerPassword.Should().Be("owner-secret");
        spec.AllowedPermissions.Should().Be(PdfPermissions.None);
    }

    [Fact]
    public void Create_NullUserPassword_Throws()
    {
        var act = () => PdfEncryptionSpec.Create(userPassword: null!, "owner", PdfPermissions.All);

        act.Should().Throw<System.ArgumentNullException>()
            .Which.ParamName.Should().Be("userPassword");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_NullOrEmptyOwnerPassword_Throws(string? ownerPassword)
    {
        var act = () => PdfEncryptionSpec.Create("u", ownerPassword!, PdfPermissions.All);

        // ArgumentException.ThrowIfNullOrEmpty бросает ArgumentNullException на null,
        // ArgumentException на "" — но обе — производные от ArgumentException.
        act.Should().Throw<System.ArgumentException>()
            .Which.ParamName.Should().Be("ownerPassword");
    }

    [Fact]
    public void Record_Equality_IsValueBased()
    {
        var a = PdfEncryptionSpec.Create("u", "o", PdfPermissions.Print);
        var b = PdfEncryptionSpec.Create("u", "o", PdfPermissions.Print);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Record_With_ProducesIndependentCopy()
    {
        var original = PdfEncryptionSpec.Create("u", "o", PdfPermissions.None);

        var modified = original with { AllowedPermissions = PdfPermissions.Print };

        modified.Should().NotBe(original);
        modified.AllowedPermissions.Should().Be(PdfPermissions.Print);
        original.AllowedPermissions.Should().Be(PdfPermissions.None);
    }
}
