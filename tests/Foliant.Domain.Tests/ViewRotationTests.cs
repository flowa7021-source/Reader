using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class ViewRotationTests
{
    [Theory]
    [InlineData(ViewRotation.None, 0)]
    [InlineData(ViewRotation.Cw90, 90)]
    [InlineData(ViewRotation.Cw180, 180)]
    [InlineData(ViewRotation.Cw270, 270)]
    public void Degrees_Maps_FromEnumValue(ViewRotation r, int expected)
    {
        r.Degrees().Should().Be(expected);
    }

    [Fact]
    public void RenderOptionsDefault_HasNoRotation()
    {
        RenderOptions.Default.Rotation.Should().Be(ViewRotation.None);
    }

    [Fact]
    public void WithRotation_KeepsOtherFields()
    {
        var initial = new RenderOptions(
            Zoom: 1.5,
            MaxWidthPx: 1000,
            MaxHeightPx: null,
            RenderAnnotations: false,
            Theme: RenderTheme.Dark);

        var rotated = initial.WithRotation(ViewRotation.Cw90);

        rotated.Should().BeEquivalentTo(initial with { Rotation = ViewRotation.Cw90 });
    }
}
