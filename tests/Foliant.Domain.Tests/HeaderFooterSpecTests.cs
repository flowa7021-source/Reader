using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class HeaderFooterSpecTests
{
    [Fact]
    public void Ctor_PreservesAllFields()
    {
        var spec = new HeaderFooterSpec("Top", "Bot", 11, 32, 64, 128);

        spec.HeaderText.Should().Be("Top");
        spec.FooterText.Should().Be("Bot");
        spec.FontSize.Should().Be(11);
        spec.R.Should().Be(32);
        spec.G.Should().Be(64);
        spec.B.Should().Be(128);
    }

    [Fact]
    public void NullsAreAllowed_ForBothTexts()
    {
        var spec = new HeaderFooterSpec(null, null, 10, 0, 0, 0);

        spec.HeaderText.Should().BeNull();
        spec.FooterText.Should().BeNull();
    }
}
