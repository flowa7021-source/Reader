using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class DiacriticFolderTests
{
    [Fact]
    public void Fold_Empty_ReturnsEmpty()
    {
        DiacriticFolder.Fold(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void Fold_Null_Throws()
    {
        Action act = () => DiacriticFolder.Fold(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("café", "cafe")]
    [InlineData("CAFÉ", "CAFE")]
    [InlineData("naïve", "naive")]
    [InlineData("Ñandú", "Nandu")]
    [InlineData("crème brûlée", "creme brulee")]
    [InlineData("ёжик", "ежик")]
    public void Fold_StripsCombiningMarks(string input, string expected)
    {
        DiacriticFolder.Fold(input).Should().Be(expected);
    }

    [Fact]
    public void Fold_AsciiOnly_IsIdentity()
    {
        DiacriticFolder.Fold("hello world 123").Should().Be("hello world 123");
    }

    [Fact]
    public void Fold_IdempotentOnFoldedInput()
    {
        string once = DiacriticFolder.Fold("résumé");
        string twice = DiacriticFolder.Fold(once);
        twice.Should().Be(once);
    }
}
