using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

/// <summary>Validation + normalization contract for <see cref="PdfPageLabelRange.Create"/>.</summary>
public sealed class PdfPageLabelRangeTests
{
    [Fact]
    public void Create_StoresAllFields()
    {
        var range = PdfPageLabelRange.Create(2, PdfPageLabelStyle.UpperRoman, "A-", 3);

        range.StartPageIndex.Should().Be(2);
        range.Style.Should().Be(PdfPageLabelStyle.UpperRoman);
        range.Prefix.Should().Be("A-");
        range.Start.Should().Be(3);
    }

    [Fact]
    public void Create_DefaultsPrefixNullAndStartOne()
    {
        var range = PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic);

        range.Prefix.Should().BeNull();
        range.Start.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_EmptyOrNullPrefix_NormalizesToNull(string? prefix)
    {
        var range = PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, prefix);

        range.Prefix.Should().BeNull();
    }

    [Fact]
    public void Create_PreservesSignificantWhitespacePrefix()
    {
        // Trailing space matters for labels like "A-1" where prefix is "A-" but also "App " variants.
        PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, "App ").Prefix.Should().Be("App ");
    }

    [Fact]
    public void Create_NoneStyle_NormalizesStartToOne()
    {
        var range = PdfPageLabelRange.Create(0, PdfPageLabelStyle.None, "Cover", start: 7);

        range.Start.Should().Be(1, "None has no numeric portion, so /St is meaningless");
    }

    [Fact]
    public void Create_NegativeStartPageIndex_Throws()
    {
        Action act = () => PdfPageLabelRange.Create(-1, PdfPageLabelStyle.Arabic);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_StartLessThanOne_Throws()
    {
        Action act = () => PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, start: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_UndefinedStyle_Throws()
    {
        Action act = () => PdfPageLabelRange.Create(0, (PdfPageLabelStyle)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Records_WithEqualValues_AreEqual()
    {
        var a = PdfPageLabelRange.Create(1, PdfPageLabelStyle.LowerRoman, "x", 2);
        var b = PdfPageLabelRange.Create(1, PdfPageLabelStyle.LowerRoman, "x", 2);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
