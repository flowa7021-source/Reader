using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

/// <summary>Value-semantics + <see cref="PdfViewerPreferences.Default"/> contract.</summary>
public sealed class PdfViewerPreferencesTests
{
    [Fact]
    public void Default_HasAllDefaultAndFalseValues()
    {
        var prefs = PdfViewerPreferences.Default;

        prefs.PageLayout.Should().Be(PdfPageLayout.Default);
        prefs.PageMode.Should().Be(PdfPageMode.Default);
        prefs.HideToolbar.Should().BeFalse();
        prefs.HideMenubar.Should().BeFalse();
        prefs.FitWindow.Should().BeFalse();
        prefs.CenterWindow.Should().BeFalse();
        prefs.DisplayDocTitle.Should().BeFalse();
    }

    [Fact]
    public void Constructor_StoresAllFields()
    {
        var prefs = new PdfViewerPreferences(
            PdfPageLayout.TwoPageLeft, PdfPageMode.UseOutlines, true, false, true, false, true);

        prefs.PageLayout.Should().Be(PdfPageLayout.TwoPageLeft);
        prefs.PageMode.Should().Be(PdfPageMode.UseOutlines);
        prefs.HideToolbar.Should().BeTrue();
        prefs.HideMenubar.Should().BeFalse();
        prefs.FitWindow.Should().BeTrue();
        prefs.CenterWindow.Should().BeFalse();
        prefs.DisplayDocTitle.Should().BeTrue();
    }

    [Fact]
    public void Records_WithEqualValues_AreEqual()
    {
        var a = new PdfViewerPreferences(
            PdfPageLayout.OneColumn, PdfPageMode.UseThumbs, true, true, false, false, true);
        var b = new PdfViewerPreferences(
            PdfPageLayout.OneColumn, PdfPageMode.UseThumbs, true, true, false, false, true);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void With_ProducesModifiedCopy()
    {
        var modified = PdfViewerPreferences.Default with
        {
            PageLayout = PdfPageLayout.TwoColumnRight,
            CenterWindow = true,
        };

        modified.PageLayout.Should().Be(PdfPageLayout.TwoColumnRight);
        modified.CenterWindow.Should().BeTrue();
        modified.Should().NotBe(PdfViewerPreferences.Default);
        modified.HideToolbar.Should().BeFalse("other fields are copied unchanged from Default");
    }
}
