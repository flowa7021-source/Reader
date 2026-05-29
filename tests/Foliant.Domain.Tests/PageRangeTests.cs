using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class PageRangeTests
{
    // ───── Parse: edge cases ─────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrBlank_ReturnsAll(string? input)
    {
        PageRange.Parse(input).IsAll.Should().BeTrue();
    }

    [Fact]
    public void All_IncludesAnyPage()
    {
        PageRange.All.Includes(0).Should().BeTrue();
        PageRange.All.Includes(999).Should().BeTrue();
    }

    // ───── Parse: single & multi segments ─────

    [Fact]
    public void Parse_SingleNumber_OneBased_To_ZeroBased()
    {
        var r = PageRange.Parse("5");
        r.IsAll.Should().BeFalse();
        r.Includes(3).Should().BeFalse();
        r.Includes(4).Should().BeTrue();   // page 5 = index 4
        r.Includes(5).Should().BeFalse();
    }

    [Fact]
    public void Parse_InclusiveRange_BothEndsIncluded()
    {
        var r = PageRange.Parse("2-4");
        r.Includes(0).Should().BeFalse();  // page 1
        r.Includes(1).Should().BeTrue();   // page 2
        r.Includes(2).Should().BeTrue();   // page 3
        r.Includes(3).Should().BeTrue();   // page 4
        r.Includes(4).Should().BeFalse();  // page 5
    }

    [Fact]
    public void Parse_MultipleSegments_CommaSeparated()
    {
        var r = PageRange.Parse("1-3,5,7-10");
        // Pages: 1,2,3,5,7,8,9,10 → indices: 0,1,2,4,6,7,8,9
        r.Includes(0).Should().BeTrue();
        r.Includes(1).Should().BeTrue();
        r.Includes(2).Should().BeTrue();
        r.Includes(3).Should().BeFalse(); // page 4
        r.Includes(4).Should().BeTrue();
        r.Includes(5).Should().BeFalse(); // page 6
        r.Includes(6).Should().BeTrue();
        r.Includes(7).Should().BeTrue();
        r.Includes(8).Should().BeTrue();
        r.Includes(9).Should().BeTrue();
        r.Includes(10).Should().BeFalse();
    }

    [Fact]
    public void Parse_SemicolonAsSeparator_AlsoWorks()
    {
        var r = PageRange.Parse("1;3;5");
        r.Includes(0).Should().BeTrue();
        r.Includes(1).Should().BeFalse();
        r.Includes(2).Should().BeTrue();
        r.Includes(3).Should().BeFalse();
        r.Includes(4).Should().BeTrue();
    }

    [Fact]
    public void Parse_WhitespaceIgnored()
    {
        var r = PageRange.Parse("  1 - 3 ,  5  ");
        r.Includes(0).Should().BeTrue();
        r.Includes(2).Should().BeTrue();
        r.Includes(4).Should().BeTrue();
        r.Includes(3).Should().BeFalse();
    }

    [Fact]
    public void Parse_OverlappingSegments_UnionSemantic()
    {
        var r = PageRange.Parse("1-5, 3-7");
        // Combined coverage: pages 1..7 → indices 0..6
        for (int i = 0; i <= 6; i++)
        {
            r.Includes(i).Should().BeTrue();
        }
        r.Includes(7).Should().BeFalse();
    }

    // ───── Parse: errors ─────

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]            // 1-based, 0 invalid
    [InlineData("-1")]           // negative
    [InlineData("3-1")]          // end < start
    [InlineData("3-")]           // missing end
    [InlineData("-5")]           // missing start (interpreted as range "0-5", start=0 invalid)
    public void Parse_Invalid_Throws(string input)
    {
        Action act = () => PageRange.Parse(input);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("1-3")]
    [InlineData("")]
    [InlineData("5,7,9")]
    public void TryParse_Valid_ReturnsTrue(string input)
    {
        bool ok = PageRange.TryParse(input, out PageRange? r);
        ok.Should().BeTrue();
        r.Should().NotBeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("3-1")]
    public void TryParse_Invalid_ReturnsFalse(string input)
    {
        bool ok = PageRange.TryParse(input, out PageRange? r);
        ok.Should().BeFalse();
        r.Should().BeNull();
    }

    [Fact]
    public void Includes_NegativeIndex_Throws()
    {
        Action act = () => PageRange.All.Includes(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
