using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class PageSizeTests
{
    [Theory]
    [InlineData(0, 595, 842)]   // 0° поворота
    [InlineData(2, 595, 842)]   // 180°
    [InlineData(4, 595, 842)]   // 360°
    public void Rotate_EvenTurns_KeepsDimensions(int turns, double w, double h)
    {
        var rotated = new PageSize(595, 842).Rotate(turns);

        rotated.Should().Be(new PageSize(w, h));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Rotate_OddTurns_SwapsDimensions(int turns)
    {
        var rotated = new PageSize(595, 842).Rotate(turns);

        rotated.Should().Be(new PageSize(842, 595));
    }

    [Fact]
    public void AspectRatio_OfA4Portrait_IsAboutSqrtHalf()
    {
        var a4 = new PageSize(595, 842);

        a4.AspectRatio.Should().BeApproximately(595.0 / 842.0, 1e-9);
    }

    [Fact]
    public void AspectRatio_OfZeroHeight_IsZero_NoDivisionByZero()
    {
        new PageSize(100, 0).AspectRatio.Should().Be(0);
    }
}
