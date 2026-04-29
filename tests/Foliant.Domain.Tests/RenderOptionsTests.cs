using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class RenderOptionsTests
{
    [Theory]
    [InlineData(1.00, 100)]
    [InlineData(1.10, 100)]   // 110 → bucket 100 (round-down при 110)
    [InlineData(1.13, 125)]   // 113 → 125
    [InlineData(0.50, 50)]
    [InlineData(2.00, 200)]
    [InlineData(0.10, 0)]   // 10 → ближайшие 25 это 0
    public void ZoomBucket_RoundsToNearest25Percent(double zoom, int expected)
    {
        var opts = RenderOptions.Default.WithZoom(zoom);

        opts.ZoomBucket().Should().Be(expected);
    }

    [Fact]
    public void Default_HasZoomOne_AndOriginalTheme()
    {
        var opts = RenderOptions.Default;

        opts.Zoom.Should().Be(1.0);
        opts.Theme.Should().Be(RenderTheme.Original);
        opts.RenderAnnotations.Should().BeTrue();
    }

    [Fact]
    public void WithZoom_KeepsOtherFieldsUnchanged()
    {
        var initial = new RenderOptions(
            Zoom: 1.0,
            MaxWidthPx: 1024,
            MaxHeightPx: 768,
            RenderAnnotations: false,
            Theme: RenderTheme.Dark);

        var updated = initial.WithZoom(2.0);

        updated.Should().BeEquivalentTo(initial with { Zoom = 2.0 });
    }
}
