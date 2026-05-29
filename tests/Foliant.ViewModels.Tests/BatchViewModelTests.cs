using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class BatchViewModelTests
{
    private static readonly WatermarkSpec SampleWatermark =
        new("DRAFT", FontSize: 48, Opacity: 0.3, AngleDegrees: 45, R: 128, G: 128, B: 128);

    private static readonly HeaderFooterSpec SampleHeaderFooter =
        new(HeaderText: "X", FooterText: "{page}", FontSize: 10, R: 0, G: 0, B: 0);

    private static readonly CropSpec SampleCrop = new(0.05, 0.10, 0.05, 0.10);

    private static BatchViewModel CreateVm(IBatchJobRunner? runner = null)
    {
        // Apply default RunAsync stub ONLY when no caller-configured runner is supplied —
        // otherwise we'd clobber the caller's .Returns(...) setup.
        if (runner is null)
        {
            runner = Substitute.For<IBatchJobRunner>();
            runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
                  .Returns(call =>
                  {
                      var jobs = (IReadOnlyList<BatchJob>)call[0];
                      return Task.FromResult<IReadOnlyList<BatchJobResult>>(
                          [.. jobs.Select(j => new BatchJobResult(j, BatchJobOutcome.Succeeded))]);
                  });
        }
        return new BatchViewModel(runner, NullLogger<BatchViewModel>.Instance);
    }

    // ───── AddFiles / RemoveFile / ClearFiles ─────

    [Fact]
    public void AddFiles_SkipsDuplicatesByPath()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf", "/b.pdf", "/a.pdf" });

        vm.Files.Should().HaveCount(2);
        vm.Files.Select(f => f.SourcePath).Should().BeEquivalentTo(new[] { "/a.pdf", "/b.pdf" });
    }

    [Fact]
    public void AddFiles_NullOrBlankPathsAreIgnored()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf", "", "   ", "/b.pdf" });

        vm.Files.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveFile_RemovesByReference()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf", "/b.pdf" });

        vm.RemoveFileCommand.Execute(vm.Files[0]);

        vm.Files.Should().ContainSingle().Which.SourcePath.Should().Be("/b.pdf");
    }

    [Fact]
    public void ClearFiles_EmptiesCollection()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf", "/b.pdf" });
        vm.ClearFilesCommand.Execute(null);

        vm.Files.Should().BeEmpty();
    }

    // ───── CanRun gate ─────

    [Fact]
    public void CanRun_NoFiles_False()
    {
        var vm = CreateVm();
        vm.CurrentSpec = SampleWatermark;
        vm.OutputFolder = "/out";
        vm.CanRun.Should().BeFalse();
    }

    [Fact]
    public void CanRun_NoSpec_False()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf" });
        vm.OutputFolder = "/out";
        vm.CanRun.Should().BeFalse();
    }

    [Fact]
    public void CanRun_NoOutputFolder_False()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf" });
        vm.CurrentSpec = SampleWatermark;
        vm.CanRun.Should().BeFalse();
    }

    [Fact]
    public void CanRun_AllConditionsMet_True()
    {
        var vm = CreateVm();
        vm.AddFilesCommand.Execute(new[] { "/a.pdf" });
        vm.CurrentSpec = SampleWatermark;
        vm.OutputFolder = "/out";
        vm.CanRun.Should().BeTrue();
    }

    // ───── BuildJobs / dispatch via runner ─────

    [Fact]
    public async Task RunBatch_Watermark_ForwardsWatermarkOperation()
    {
        var runner = Substitute.For<IBatchJobRunner>();
        IReadOnlyList<BatchJob>? capturedJobs = null;
        runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  capturedJobs = (IReadOnlyList<BatchJob>)call[0];
                  return Task.FromResult<IReadOnlyList<BatchJobResult>>(
                      [.. capturedJobs.Select(j => new BatchJobResult(j, BatchJobOutcome.Succeeded))]);
              });

        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/in/a.pdf", "/in/b.pdf" });
        vm.Operation = BatchOperationKind.Watermark;
        vm.CurrentSpec = SampleWatermark;
        vm.OutputFolder = "/out";

        await vm.RunBatchCommand.ExecuteAsync(null);

        capturedJobs.Should().NotBeNull();
        var jobs = capturedJobs!;
        jobs.Should().HaveCount(2);
        jobs[0].Operation.Should().BeOfType<BatchWatermarkOperation>()
            .Which.Spec.Should().BeSameAs(SampleWatermark);
        jobs[0].TargetPath.Should().Be(Path.Combine("/out", "a-watermarked.pdf"));
        jobs[1].TargetPath.Should().Be(Path.Combine("/out", "b-watermarked.pdf"));
    }

    [Fact]
    public async Task RunBatch_HeaderFooter_UsesHeaderFooterSuffix()
    {
        var runner = Substitute.For<IBatchJobRunner>();
        IReadOnlyList<BatchJob>? capturedJobs = null;
        runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  capturedJobs = (IReadOnlyList<BatchJob>)call[0];
                  return Task.FromResult<IReadOnlyList<BatchJobResult>>([]);
              });

        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/in/doc.pdf" });
        vm.Operation = BatchOperationKind.HeaderFooter;
        vm.CurrentSpec = SampleHeaderFooter;
        vm.OutputFolder = "/out";

        await vm.RunBatchCommand.ExecuteAsync(null);

        var jobs = capturedJobs!;
        jobs[0].Operation.Should().BeOfType<BatchHeaderFooterOperation>();
        jobs[0].TargetPath.Should().EndWith("doc-headerfooter.pdf");
    }

    [Fact]
    public async Task RunBatch_Crop_UsesCropSuffix()
    {
        var runner = Substitute.For<IBatchJobRunner>();
        IReadOnlyList<BatchJob>? capturedJobs = null;
        runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  capturedJobs = (IReadOnlyList<BatchJob>)call[0];
                  return Task.FromResult<IReadOnlyList<BatchJobResult>>([]);
              });

        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/in/page.pdf" });
        vm.Operation = BatchOperationKind.Crop;
        vm.CurrentSpec = SampleCrop;
        vm.OutputFolder = "/out";

        await vm.RunBatchCommand.ExecuteAsync(null);

        var jobs = capturedJobs!;
        jobs[0].Operation.Should().BeOfType<BatchCropOperation>();
        jobs[0].TargetPath.Should().EndWith("page-cropped.pdf");
    }

    // ───── Mismatched spec/operation ─────

    [Fact]
    public async Task RunBatch_SpecOperationMismatch_NoRun_NoCrash()
    {
        var runner = Substitute.For<IBatchJobRunner>();
        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/a.pdf" });
        vm.Operation = BatchOperationKind.Watermark;
        vm.CurrentSpec = SampleCrop;          // ← wrong spec for the chosen op
        vm.OutputFolder = "/out";

        await vm.RunBatchCommand.ExecuteAsync(null);

        await runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default);
        vm.IsRunning.Should().BeFalse();
    }

    // ───── Result application ─────

    [Fact]
    public async Task RunBatch_AppliesResultsToRows()
    {
        var runner = Substitute.For<IBatchJobRunner>();
        runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  var jobs = (IReadOnlyList<BatchJob>)call[0];
                  return Task.FromResult<IReadOnlyList<BatchJobResult>>(
                  [
                      new BatchJobResult(jobs[0], BatchJobOutcome.Succeeded),
                      new BatchJobResult(jobs[1], BatchJobOutcome.Failed, "corrupt"),
                  ]);
              });

        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/a.pdf", "/b.pdf" });
        vm.CurrentSpec = SampleWatermark;
        vm.OutputFolder = "/out";

        await vm.RunBatchCommand.ExecuteAsync(null);

        vm.Files[0].Outcome.Should().Be(BatchJobOutcome.Succeeded);
        vm.Files[1].Outcome.Should().Be(BatchJobOutcome.Failed);
        vm.Files[1].Error.Should().Be("corrupt");
    }

    [Fact]
    public async Task RunBatch_RunnerThrows_DoesNotCrashAndResetsIsRunning()
    {
        var runner = Substitute.For<IBatchJobRunner>();
        runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromException<IReadOnlyList<BatchJobResult>>(new InvalidOperationException("boom")));

        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/a.pdf" });
        vm.CurrentSpec = SampleWatermark;
        vm.OutputFolder = "/out";

        Func<Task> act = async () => await vm.RunBatchCommand.ExecuteAsync(null);
        await act.Should().NotThrowAsync();
        vm.IsRunning.Should().BeFalse();
    }

    // ───── Cancel ─────

    [Fact]
    public async Task CancelBatch_AllowsRunnerCancellation()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<BatchJobResult>>();
        var runner = Substitute.For<IBatchJobRunner>();
        CancellationToken capturedCt = default;
        runner.RunAsync(Arg.Any<IReadOnlyList<BatchJob>>(), Arg.Any<int>(), Arg.Any<IProgress<BatchProgress>?>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  capturedCt = (CancellationToken)call[3];
                  capturedCt.Register(() => tcs.TrySetCanceled());
                  return tcs.Task;
              });

        var vm = CreateVm(runner);
        vm.AddFilesCommand.Execute(new[] { "/a.pdf" });
        vm.CurrentSpec = SampleWatermark;
        vm.OutputFolder = "/out";

        var runTask = vm.RunBatchCommand.ExecuteAsync(null);
        await Task.Yield(); // let RunBatchAsync set IsRunning = true and pass us the cts
        vm.IsRunning.Should().BeTrue();

        vm.CancelBatchCommand.Execute(null);
        await runTask;

        vm.IsRunning.Should().BeFalse();
        capturedCt.IsCancellationRequested.Should().BeTrue();
    }
}
