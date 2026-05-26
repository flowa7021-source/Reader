using FluentAssertions;
using Xunit;

namespace Foliant.Engines.Ocr.Tests;

public sealed class CharacterErrorRateTests
{
    [Fact]
    public void Cer_IdenticalStrings_IsZero()
    {
        CharacterErrorRate.Cer("the quick brown fox", "the quick brown fox").Should().Be(0.0);
    }

    [Fact]
    public void Cer_FullReplacement_IsOne()
    {
        CharacterErrorRate.Cer("aaaa", "bbbb").Should().Be(1.0);
    }

    [Theory]
    [InlineData("abc", "abxc")] // single insertion
    [InlineData("abc", "ac")]   // single deletion
    [InlineData("abc", "axc")]  // single substitution
    public void Cer_SingleEditOnThreeChars_IsOneThird(string reference, string hypothesis)
    {
        CharacterErrorRate.Cer(reference, hypothesis).Should().BeApproximately(1.0 / 3.0, 1e-9);
    }

    [Fact]
    public void Cer_EmptyReferenceWithEmptyHypothesis_IsZero()
    {
        CharacterErrorRate.Cer(string.Empty, string.Empty).Should().Be(0.0);
    }

    [Fact]
    public void Cer_EmptyReferenceWithText_DividesByOneGuard()
    {
        CharacterErrorRate.Cer(string.Empty, "xyz").Should().Be(3.0);
    }

    [Fact]
    public void Cer_HelloVsHallo_IsZeroPointTwo()
    {
        CharacterErrorRate.Cer("hello", "hallo").Should().BeApproximately(0.2, 1e-9);
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("kitten", "sitting", 3)]
    public void EditDistance_KnownCases(string a, string b, int expected)
    {
        CharacterErrorRate.EditDistance(a, b).Should().Be(expected);
    }
}
