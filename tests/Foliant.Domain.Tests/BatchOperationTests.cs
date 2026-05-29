using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class BatchOperationTests
{
    private static readonly WatermarkSpec SampleWatermark =
        new("DRAFT", FontSize: 48, Opacity: 0.3, AngleDegrees: 45, R: 128, G: 128, B: 128);

    private static readonly HeaderFooterSpec SampleHeaderFooter =
        new(HeaderText: "Doc", FooterText: "{page}/{total}", FontSize: 10, R: 64, G: 64, B: 64);

    [Fact]
    public void ApplyWatermark_CarriesSpec()
    {
        var op = new BatchWatermarkOperation(SampleWatermark);
        op.Spec.Should().BeSameAs(SampleWatermark);
    }

    [Fact]
    public void ApplyHeaderFooter_CarriesSpec()
    {
        var op = new BatchHeaderFooterOperation(SampleHeaderFooter);
        op.Spec.Should().BeSameAs(SampleHeaderFooter);
    }

    [Fact]
    public void ApplyCrop_CarriesSpec()
    {
        var spec = new CropSpec(0.05, 0.10, 0.05, 0.10);
        var op = new BatchCropOperation(spec);
        op.Spec.Should().BeSameAs(spec);
    }

    [Fact]
    public void BatchJob_IsRecord_StructuralEquality()
    {
        var a = new BatchJob("/in", new BatchWatermarkOperation(SampleWatermark), "/out");
        var b = new BatchJob("/in", new BatchWatermarkOperation(SampleWatermark), "/out");
        a.Should().Be(b);
    }

    [Fact]
    public void BatchJobResult_DefaultErrorIsNull()
    {
        var job = new BatchJob("/in", new BatchWatermarkOperation(SampleWatermark), "/out");
        var result = new BatchJobResult(job, BatchJobOutcome.Succeeded);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void BatchProgress_CarriesCountsAndLast()
    {
        var job = new BatchJob("/in", new BatchHeaderFooterOperation(SampleHeaderFooter), "/out");
        var result = new BatchJobResult(job, BatchJobOutcome.Failed, "boom");
        var p = new BatchProgress(3, 10, result);
        p.Completed.Should().Be(3);
        p.Total.Should().Be(10);
        p.LastResult.Should().BeSameAs(result);
    }
}
