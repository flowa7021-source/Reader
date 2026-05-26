using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class PageGeometryTests
{
    private static readonly PageSize Letter = new(612, 792); // US Letter, points

    [Theory]
    [InlineData(1.0, 1.3333333)]
    [InlineData(2.0, 2.6666666)]
    [InlineData(0.5, 0.6666666)]
    public void PixelsPerPoint_ScalesBy96Over72(double zoom, double expected)
    {
        PageGeometry.PixelsPerPoint(zoom).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void PointToPixel_TopLeftCorner_MapsToPixelOrigin()
    {
        // PDF top-left = (0, HeightPt); should map to pixel (0, 0).
        var (x, y) = PageGeometry.PointToPixel(0, Letter.HeightPt, Letter, 1.0);

        x.Should().BeApproximately(0, 1e-6);
        y.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void PointToPixel_BottomLeftCorner_MapsToPageHeightInPixels()
    {
        var (x, y) = PageGeometry.PointToPixel(0, 0, Letter, 1.0);

        x.Should().BeApproximately(0, 1e-6);
        y.Should().BeApproximately(792 * 96.0 / 72.0, 1e-6);
    }

    [Fact]
    public void ToPixels_LowerLeftRect_FlipsYToTopLeft()
    {
        // Rect anchored at PDF lower-left (0,0), 100x50 pt, page 792pt tall, zoom 1.0.
        var px = PageGeometry.ToPixels(new AnnotationRect(0, 0, 100, 50), Letter, 1.0);
        double scale = 96.0 / 72.0;

        px.X.Should().BeApproximately(0, 1e-6);
        px.Y.Should().BeApproximately((792 - 50) * scale, 1e-6);
        px.Width.Should().BeApproximately(100 * scale, 1e-6);
        px.Height.Should().BeApproximately(50 * scale, 1e-6);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(3.25)]
    public void ToPixels_ThenToPoints_RoundTrips(double zoom)
    {
        var original = new AnnotationRect(100, 200, 50, 30);

        var px = PageGeometry.ToPixels(original, Letter, zoom);
        var back = PageGeometry.ToPoints(px, Letter, zoom);

        back.X.Should().BeApproximately(original.X, 1e-6);
        back.Y.Should().BeApproximately(original.Y, 1e-6);
        back.Width.Should().BeApproximately(original.Width, 1e-6);
        back.Height.Should().BeApproximately(original.Height, 1e-6);
    }

    [Fact]
    public void PixelToPoint_ThenPointToPixel_RoundTrips()
    {
        var (xPt, yPt) = PageGeometry.PixelToPoint(300, 400, Letter, 2.0);
        var (xPx, yPx) = PageGeometry.PointToPixel(xPt, yPt, Letter, 2.0);

        xPx.Should().BeApproximately(300, 1e-6);
        yPx.Should().BeApproximately(400, 1e-6);
    }

}
