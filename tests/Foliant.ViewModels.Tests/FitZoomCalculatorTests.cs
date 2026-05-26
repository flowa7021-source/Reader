using FluentAssertions;
using Foliant.Domain;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class FitZoomCalculatorTests
{
    private static readonly PageSize Letter = new(612, 792); // 8.5x11" in points

    [Fact]
    public void ActualSize_ReturnsNull()
    {
        FitZoomCalculator.Compute(FitMode.ActualSize, Letter, 1224, 1000, 0.1, 8.0)
            .Should().BeNull();
    }

    [Fact]
    public void FitWidth_ScalesPageWidthToViewport()
    {
        // 612pt * 96/72 = 816px at zoom 1.0; viewport 1224 → zoom 1.5
        FitZoomCalculator.Compute(FitMode.FitWidth, Letter, 1224, 5000, 0.1, 8.0)
            .Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void FitPage_TakesTheSmallerOfWidthAndHeightFit()
    {
        // height fit: 792pt * 96/72 = 1056px at zoom 1.0; viewport height 528 → 0.5 < width-fit 1.5
        FitZoomCalculator.Compute(FitMode.FitPage, Letter, 1224, 528, 0.1, 8.0)
            .Should().BeApproximately(0.5, 1e-9);
    }

    [Theory]
    [InlineData(0, 0)]      // zero width/height page
    [InlineData(612, 0)]
    public void InvalidPageSize_ReturnsNull(double w, double h)
    {
        FitZoomCalculator.Compute(FitMode.FitWidth, new PageSize(w, h), 1224, 1000, 0.1, 8.0)
            .Should().BeNull();
    }

    [Fact]
    public void NonPositiveViewport_ReturnsNull()
    {
        FitZoomCalculator.Compute(FitMode.FitWidth, Letter, 0, 1000, 0.1, 8.0)
            .Should().BeNull();
    }

    [Fact]
    public void Result_IsClampedToMaxZoom()
    {
        FitZoomCalculator.Compute(FitMode.FitWidth, Letter, 100_000, 5000, 0.1, 8.0)
            .Should().Be(8.0);
    }

    [Fact]
    public void Result_IsClampedToMinZoom()
    {
        FitZoomCalculator.Compute(FitMode.FitWidth, Letter, 10, 5000, 0.1, 8.0)
            .Should().Be(0.1);
    }
}
