using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class BatchJobRunnerTests
{
    private static readonly WatermarkSpec SampleWatermark =
        new("DRAFT", FontSize: 48, Opacity: 0.3, AngleDegrees: 45, R: 128, G: 128, B: 128);

    private static readonly HeaderFooterSpec SampleHeaderFooter =
        new(HeaderText: "Doc", FooterText: "{page}/{total}", FontSize: 10, R: 64, G: 64, B: 64);

    private static readonly CropSpec SampleCrop = new(0.05, 0.10, 0.05, 0.10);

    private static BatchJobRunner Create(
        IWatermarkService? watermark = null,
        IHeaderFooterService? headerFooter = null,
        IPdfCropService? crop = null) =>
        new(NullLogger<BatchJobRunner>.Instance, watermark, headerFooter, crop);

    private static BatchJob WatermarkJob(string source = "/in.pdf", string target = "/out.pdf") =>
        new(source, new BatchWatermarkOperation(SampleWatermark), target);

    private static BatchJob HeaderFooterJob(string source = "/in.pdf", string target = "/out.pdf") =>
        new(source, new BatchHeaderFooterOperation(SampleHeaderFooter), target);

    private static BatchJob CropJob(string source = "/in.pdf", string target = "/out.pdf") =>
        new(source, new BatchCropOperation(SampleCrop), target);

    // ───── Empty / no-op ─────

    [Fact]
    public async Task RunAsync_EmptyList_ReturnsEmpty()
    {
        var result = await Create().RunAsync([], maxParallelism: 4, progress: null, CancellationToken.None);
        result.Should().BeEmpty();
    }

    // ───── Dispatch correctness ─────

    [Fact]
    public async Task RunAsync_WatermarkJob_DispatchesToWatermarkService()
    {
        var wm = Substitute.For<IWatermarkService>();
        var job = WatermarkJob();

        var results = await Create(watermark: wm).RunAsync([job], 1, null, CancellationToken.None);

        await wm.Received(1).ApplyAsync("/in.pdf", SampleWatermark, "/out.pdf", Arg.Any<CancellationToken>());
        results.Should().ContainSingle().Which.Outcome.Should().Be(BatchJobOutcome.Succeeded);
    }

    [Fact]
    public async Task RunAsync_HeaderFooterJob_DispatchesToHeaderFooterService()
    {
        var hf = Substitute.For<IHeaderFooterService>();
        var job = HeaderFooterJob();

        var results = await Create(headerFooter: hf).RunAsync([job], 1, null, CancellationToken.None);

        await hf.Received(1).ApplyAsync("/in.pdf", SampleHeaderFooter, "/out.pdf", Arg.Any<CancellationToken>());
        results.Should().ContainSingle().Which.Outcome.Should().Be(BatchJobOutcome.Succeeded);
    }

    [Fact]
    public async Task RunAsync_CropJob_DispatchesToCropService()
    {
        var crop = Substitute.For<IPdfCropService>();
        var job = CropJob();

        var results = await Create(crop: crop).RunAsync([job], 1, null, CancellationToken.None);

        await crop.Received(1).ApplyAsync("/in.pdf", SampleCrop, "/out.pdf", Arg.Any<CancellationToken>());
        results.Should().ContainSingle().Which.Outcome.Should().Be(BatchJobOutcome.Succeeded);
    }

    [Fact]
    public async Task RunAsync_CropJob_MissingService_ProducesFailedResult()
    {
        var runner = Create(crop: null);
        var results = await runner.RunAsync([CropJob()], 1, null, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Outcome.Should().Be(BatchJobOutcome.Failed);
        results[0].Error.Should().Contain("IPdfCropService");
    }

    [Fact]
    public async Task RunAsync_MissingService_ProducesFailedResult()
    {
        // No watermark service registered → first job should fail with a clear message.
        var runner = Create(watermark: null);
        var results = await runner.RunAsync([WatermarkJob()], 1, null, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Outcome.Should().Be(BatchJobOutcome.Failed);
        results[0].Error.Should().Contain("IWatermarkService");
    }

    // ───── Per-job isolation ─────

    [Fact]
    public async Task RunAsync_OneJobThrows_OthersStillRun()
    {
        var wm = Substitute.For<IWatermarkService>();
        wm.ApplyAsync(Arg.Is<string>(s => s == "/bad.pdf"), Arg.Any<WatermarkSpec>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromException(new InvalidOperationException("corrupt source")));

        var jobs = new[]
        {
            WatermarkJob("/ok1.pdf", "/out1.pdf"),
            WatermarkJob("/bad.pdf", "/bad-out.pdf"),
            WatermarkJob("/ok2.pdf", "/out2.pdf"),
        };

        var results = await Create(watermark: wm).RunAsync(jobs, 1, null, CancellationToken.None);

        results.Should().HaveCount(3);
        results[0].Outcome.Should().Be(BatchJobOutcome.Succeeded);
        results[1].Outcome.Should().Be(BatchJobOutcome.Failed);
        results[1].Error.Should().Contain("corrupt source");
        results[2].Outcome.Should().Be(BatchJobOutcome.Succeeded);
    }

    // ───── Result ordering ─────

    [Fact]
    public async Task RunAsync_ResultsStableInJobOrder_EvenWhenParallel()
    {
        var wm = Substitute.For<IWatermarkService>();
        // Make later-indexed jobs finish first to scramble completion order.
        wm.ApplyAsync(Arg.Any<string>(), Arg.Any<WatermarkSpec>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(async call =>
          {
              string source = (string)call[0];
              int waitMs = source switch
              {
                  "/in0.pdf" => 50,
                  "/in1.pdf" => 30,
                  "/in2.pdf" => 10,
                  _ => 0,
              };
              await Task.Delay(waitMs);
          });

        var jobs = new[]
        {
            WatermarkJob("/in0.pdf", "/out0.pdf"),
            WatermarkJob("/in1.pdf", "/out1.pdf"),
            WatermarkJob("/in2.pdf", "/out2.pdf"),
        };

        var results = await Create(watermark: wm).RunAsync(jobs, maxParallelism: 4, progress: null, CancellationToken.None);

        results.Should().HaveCount(3);
        results[0].Job.SourcePath.Should().Be("/in0.pdf");
        results[1].Job.SourcePath.Should().Be("/in1.pdf");
        results[2].Job.SourcePath.Should().Be("/in2.pdf");
    }

    // ───── Progress reporting ─────

    [Fact]
    public async Task RunAsync_ReportsProgressOncePerJob()
    {
        var wm = Substitute.For<IWatermarkService>();
        var progress = new TestProgress();

        var jobs = new[]
        {
            WatermarkJob("/a.pdf", "/oa.pdf"),
            WatermarkJob("/b.pdf", "/ob.pdf"),
            WatermarkJob("/c.pdf", "/oc.pdf"),
        };

        await Create(watermark: wm).RunAsync(jobs, 1, progress, CancellationToken.None);

        progress.Reports.Should().HaveCount(3);
        progress.Reports.Select(r => r.Total).Should().AllBeEquivalentTo(3);
        progress.Reports.Select(r => r.Completed).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    // ───── Cancellation ─────

    [Fact]
    public async Task RunAsync_AlreadyCancelledToken_MarksAllJobsCancelled()
    {
        var wm = Substitute.For<IWatermarkService>();
        var jobs = new[] { WatermarkJob("/a.pdf"), WatermarkJob("/b.pdf") };

        var results = await Create(watermark: wm).RunAsync(jobs, 1, null, new CancellationToken(canceled: true));

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Outcome == BatchJobOutcome.Cancelled);
        await wm.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RunAsync_NonPositiveParallelism_FallsBackToSequential()
    {
        var wm = Substitute.For<IWatermarkService>();
        var results = await Create(watermark: wm).RunAsync(
            [WatermarkJob("/x.pdf", "/y.pdf")], maxParallelism: 0, progress: null, CancellationToken.None);
        results.Should().ContainSingle().Which.Outcome.Should().Be(BatchJobOutcome.Succeeded);
    }

    private sealed class TestProgress : IProgress<BatchProgress>
    {
        private readonly Lock _gate = new();
        public List<BatchProgress> Reports { get; } = [];

        public void Report(BatchProgress value)
        {
            lock (_gate)
            {
                Reports.Add(value);
            }
        }
    }
}
