using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class PdfAComplianceResultTests
{
    [Fact]
    public void PdfAComplianceResult_PreservesFields()
    {
        var issues = new[]
        {
            new PdfAValidationIssue("6.7.3-2", "XMP packet missing PDF/A identifier.", PageIndex: null),
            new PdfAValidationIssue("6.2.2-1", "Embedded font is not allowed.", PageIndex: 4),
        };

        var result = new PdfAComplianceResult("PDF/A-1B", IsCompliant: false, Issues: issues);

        result.Profile.Should().Be("PDF/A-1B");
        result.IsCompliant.Should().BeFalse();
        result.Issues.Should().HaveCount(2);
        result.Issues[0].RuleId.Should().Be("6.7.3-2");
        result.Issues[0].PageIndex.Should().BeNull();
        result.Issues[1].PageIndex.Should().Be(4);
    }

    [Fact]
    public void PdfAComplianceResult_EqualityIsValueBased()
    {
        var a = new PdfAComplianceResult("PDF/A-1B", IsCompliant: true, Issues: Array.Empty<PdfAValidationIssue>());
        var b = new PdfAComplianceResult("PDF/A-1B", IsCompliant: true, Issues: Array.Empty<PdfAValidationIssue>());

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(a with { Profile = "PDF/A-2B" });
        a.Should().NotBe(a with { IsCompliant = false });
    }

    [Fact]
    public void PdfAValidationIssue_EqualityIsValueBased()
    {
        var a = new PdfAValidationIssue("6.2.2-1", "Embedded font is not allowed.", PageIndex: 4);
        var b = new PdfAValidationIssue("6.2.2-1", "Embedded font is not allowed.", PageIndex: 4);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(a with { PageIndex = 5 });
        a.Should().NotBe(a with { RuleId = "6.2.2-2" });
        a.Should().NotBe(a with { PageIndex = null });
    }

    [Fact]
    public void PdfAValidationIssue_NullPageIndex_IsAllowedForDocumentLevel()
    {
        // Document-level issues (XMP, OutputIntent) have no page → PageIndex must accept null.
        var issue = new PdfAValidationIssue("6.7.3-1", "Document does not contain XMP metadata.", PageIndex: null);

        issue.PageIndex.Should().BeNull();
        issue.RuleId.Should().Be("6.7.3-1");
    }
}
