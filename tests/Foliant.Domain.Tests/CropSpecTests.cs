using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class CropSpecTests
{
    // ───── HasEffect ─────

    [Fact]
    public void HasEffect_AllZero_False()
    {
        new CropSpec(0, 0, 0, 0).HasEffect.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.001, 0, 0, 0)]
    [InlineData(0, 0.004, 0, 0)]
    public void HasEffect_BelowFractionThreshold_False(double l, double t, double r, double b)
    {
        new CropSpec(l, t, r, b).HasEffect.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.01, 0, 0, 0)]
    [InlineData(0, 0.05, 0, 0)]
    [InlineData(0, 0, 0.10, 0)]
    [InlineData(0, 0, 0, 0.20)]
    public void HasEffect_OverThreshold_True(double l, double t, double r, double b)
    {
        new CropSpec(l, t, r, b).HasEffect.Should().BeTrue();
    }

    // ───── Validate ─────

    [Fact]
    public void Validate_ZeroSpec_DoesNotThrow()
    {
        new CropSpec(0, 0, 0, 0).Validate(); // no throw
    }

    [Fact]
    public void Validate_HalfOneSideOnly_DoesNotThrow()
    {
        new CropSpec(0.5, 0, 0, 0).Validate(); // per-side max is 0.5; L+R = 0.5 < 0.95, OK
    }

    [Theory]
    [InlineData(-0.01, 0, 0, 0)]
    [InlineData(0, -0.5, 0, 0)]
    [InlineData(0.51, 0, 0, 0)]
    [InlineData(0, 0, 0.6, 0)]
    [InlineData(double.NaN, 0, 0, 0)]
    public void Validate_OutOfRangeSide_Throws(double l, double t, double r, double b)
    {
        Action act = () => new CropSpec(l, t, r, b).Validate();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.5, 0, 0.5, 0)]     // L+R = 1.0
    [InlineData(0.48, 0, 0.48, 0)]   // L+R = 0.96
    public void Validate_LeftPlusRightTooLarge_Throws(double l, double t, double r, double b)
    {
        Action act = () => new CropSpec(l, t, r, b).Validate();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 0.5, 0, 0.5)]
    [InlineData(0, 0.48, 0, 0.48)]
    public void Validate_TopPlusBottomTooLarge_Throws(double l, double t, double r, double b)
    {
        Action act = () => new CropSpec(l, t, r, b).Validate();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_SymmetricFivePercent_DoesNotThrow()
    {
        new CropSpec(0.05, 0.10, 0.05, 0.10).Validate(); // no throw
    }
}
