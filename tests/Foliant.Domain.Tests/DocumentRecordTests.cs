using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class DocumentRecordTests
{
    [Fact]
    public void DocumentCommandRecord_PreservesKindAndPayload_AndIsValueEqual()
    {
        var a = new DocumentCommandRecord("InsertPage", "{\"at\":3}");
        var b = new DocumentCommandRecord("InsertPage", "{\"at\":3}");

        a.Kind.Should().Be("InsertPage");
        a.PayloadJson.Should().Be("{\"at\":3}");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(a with { Kind = "RotatePage" });
    }

    [Fact]
    public void DocumentMetadata_Empty_HasAllNullFields_AndEmptyCustom()
    {
        var empty = DocumentMetadata.Empty;

        empty.Title.Should().BeNull();
        empty.Author.Should().BeNull();
        empty.Subject.Should().BeNull();
        empty.Created.Should().BeNull();
        empty.Modified.Should().BeNull();
        empty.Custom.Should().BeEmpty();
    }

    [Fact]
    public void DocumentMetadata_Empty_IsSharedInstance()
    {
        DocumentMetadata.Empty.Should().BeSameAs(DocumentMetadata.Empty);
    }

    [Fact]
    public void DocumentMetadata_PreservesFields_AndWithCopiesRest()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var meta = new DocumentMetadata(
            "Title",
            "Author",
            "Subject",
            created,
            created.AddDays(1),
            new Dictionary<string, string> { ["k"] = "v" });

        meta.Title.Should().Be("Title");
        meta.Custom.Should().ContainKey("k").WhoseValue.Should().Be("v");

        var renamed = meta with { Title = "New" };
        renamed.Title.Should().Be("New");
        renamed.Author.Should().Be("Author");
        renamed.Created.Should().Be(created);
    }

    [Theory]
    [InlineData(FormFieldKind.Text)]
    [InlineData(FormFieldKind.Checkbox)]
    [InlineData(FormFieldKind.Signature)]
    public void FormField_PreservesKindAndFlags(FormFieldKind kind)
    {
        var field = new FormField("agree", kind, Value: "yes", IsRequired: true, IsReadOnly: false);

        field.Name.Should().Be("agree");
        field.Kind.Should().Be(kind);
        field.Value.Should().Be("yes");
        field.IsRequired.Should().BeTrue();
        field.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void FormField_EqualityIsValueBased()
    {
        var a = new FormField("n", FormFieldKind.Choice, null, false, true);
        var b = new FormField("n", FormFieldKind.Choice, null, false, true);

        a.Should().Be(b);
        a.Should().NotBe(b with { IsReadOnly = false });
    }

    [Fact]
    public void DocumentSignature_PreservesFields_AndEquality()
    {
        var signedAt = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero);
        var a = new DocumentSignature("Alice", signedAt, "Approval", "Berlin", SignatureKind.PadesLTA);
        var b = new DocumentSignature("Alice", signedAt, "Approval", "Berlin", SignatureKind.PadesLTA);

        a.SignerName.Should().Be("Alice");
        a.SignedAt.Should().Be(signedAt);
        a.Reason.Should().Be("Approval");
        a.Location.Should().Be("Berlin");
        a.Kind.Should().Be(SignatureKind.PadesLTA);
        a.Should().Be(b);
        a.Should().NotBe(b with { Kind = SignatureKind.Gost });
    }

    [Theory]
    [InlineData(true, true, true, null)]
    [InlineData(false, false, false, "tampered after signing")]
    public void SignatureValidationResult_PreservesFlagsAndReason(
        bool isValid, bool trusted, bool untouched, string? reason)
    {
        var result = new SignatureValidationResult(isValid, trusted, untouched, reason);

        result.IsValid.Should().Be(isValid);
        result.CertificateTrusted.Should().Be(trusted);
        result.DocumentUntouchedSinceSigning.Should().Be(untouched);
        result.FailureReason.Should().Be(reason);
    }
}
