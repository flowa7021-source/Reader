using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class WatermarkSpecTests
{
    [Fact]
    public void Ctor_PreservesAllFields()
    {
        var spec = new WatermarkSpec("CONFIDENTIAL", 64, 0.3, 45, 128, 64, 32);

        spec.Text.Should().Be("CONFIDENTIAL");
        spec.FontSize.Should().Be(64);
        spec.Opacity.Should().Be(0.3);
        spec.AngleDegrees.Should().Be(45);
        spec.R.Should().Be(128);
        spec.G.Should().Be(64);
        spec.B.Should().Be(32);
    }

    [Fact]
    public void Records_StructuralEquality()
    {
        var a = new WatermarkSpec("x", 1, 0.5, 0, 0, 0, 0);
        var b = new WatermarkSpec("x", 1, 0.5, 0, 0, 0, 0);

        a.Should().Be(b);
    }

    [Fact]
    public void ImagePath_DefaultsToNull()
    {
        new WatermarkSpec("x", 1, 0.5, 0, 0, 0, 0).ImagePath.Should().BeNull();
    }

    [Fact]
    public void ImagePath_IsCarriedForImageMode()
    {
        var spec = new WatermarkSpec("", 1, 0.3, 0, 0, 0, 0, Range: null, ImagePath: "/tmp/stamp.png");
        spec.ImagePath.Should().Be("/tmp/stamp.png");
    }
}
