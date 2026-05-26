using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class AnnotationGeometryTests
{
    [Theory]
    [InlineData(10, 20, 30, 5)]   // a above-left of b
    [InlineData(30, 5, 10, 20)]   // reversed order — same rect
    [InlineData(10, 5, 30, 20)]   // a below-left of b
    public void RectFromPoints_NormalizesToBottomLeftAndSize(double ax, double ay, double bx, double by)
    {
        var rect = PageGeometry.RectFromPoints(new AnnotationPoint(ax, ay), new AnnotationPoint(bx, by));

        rect.X.Should().Be(10);
        rect.Y.Should().Be(5);
        rect.Width.Should().Be(20);
        rect.Height.Should().Be(15);
    }

    [Fact]
    public void RectFromPoints_SamePoint_IsZeroSize()
    {
        var rect = PageGeometry.RectFromPoints(new AnnotationPoint(7, 7), new AnnotationPoint(7, 7));

        rect.Should().Be(new AnnotationRect(7, 7, 0, 0));
    }

    [Fact]
    public void Simplify_TwoOrFewerPoints_ReturnedUnchanged()
    {
        IReadOnlyList<AnnotationPoint> pts = [new(0, 0), new(5, 5)];

        FreehandGeometry.Simplify(pts, 0.5).Should().BeSameAs(pts);
    }

    [Fact]
    public void Simplify_NonPositiveEpsilon_ReturnedUnchanged()
    {
        IReadOnlyList<AnnotationPoint> pts = [new(0, 0), new(1, 9), new(2, 0)];

        FreehandGeometry.Simplify(pts, 0).Should().BeSameAs(pts);
    }

    [Fact]
    public void Simplify_CollinearPoints_ReducedToEndpoints()
    {
        IReadOnlyList<AnnotationPoint> pts = [new(0, 0), new(1, 1), new(2, 2), new(3, 3)];

        var result = FreehandGeometry.Simplify(pts, 0.1);

        result.Should().Equal(new AnnotationPoint(0, 0), new AnnotationPoint(3, 3));
    }

    [Fact]
    public void Simplify_KeepsPointDeviatingBeyondEpsilon()
    {
        IReadOnlyList<AnnotationPoint> pts = [new(0, 0), new(1, 5), new(2, 0)];

        var result = FreehandGeometry.Simplify(pts, 1.0);

        result.Should().Equal(new AnnotationPoint(0, 0), new AnnotationPoint(1, 5), new AnnotationPoint(2, 0));
    }

    [Fact]
    public void Simplify_ClosedLoop_KeepsPointFarFromCoincidentEndpoints()
    {
        // first == last (degenerate segment): distance falls back to point-distance from the endpoint.
        IReadOnlyList<AnnotationPoint> pts = [new(0, 0), new(1, 5), new(0, 0)];

        var result = FreehandGeometry.Simplify(pts, 1.0);

        result.Should().Equal(new AnnotationPoint(0, 0), new AnnotationPoint(1, 5), new AnnotationPoint(0, 0));
    }

    [Fact]
    public void Simplify_DropsPointWithinEpsilon()
    {
        // Middle point deviates only 0.5 from the 0,0→2,0 line; epsilon 1.0 → dropped.
        IReadOnlyList<AnnotationPoint> pts = [new(0, 0), new(1, 0.5), new(2, 0)];

        var result = FreehandGeometry.Simplify(pts, 1.0);

        result.Should().Equal(new AnnotationPoint(0, 0), new AnnotationPoint(2, 0));
    }
}
