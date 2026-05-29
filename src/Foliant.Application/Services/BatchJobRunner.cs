using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Application.Services;

/// <summary>
/// Default <see cref="IBatchJobRunner"/> — dispatches each <see cref="BatchJob"/>'s operation to
/// the matching per-operation service, then aggregates outcomes. Parallelism bounded via
/// <c>Parallel.ForEachAsync</c>'s <c>ParallelOptions.MaxDegreeOfParallelism</c>; exceptions are
/// caught per-job inside the body so they don't tear down the batch.
///
/// Resolution: any of the per-operation services can be <c>null</c> (Phase-1 builds where one
/// engine isn't wired yet) — the corresponding op then completes as
/// <see cref="BatchJobOutcome.Failed"/> with a "not configured" message rather than blowing up
/// the whole batch.
/// </summary>
public sealed class BatchJobRunner : IBatchJobRunner
{
    private readonly IWatermarkService? _watermark;
    private readonly IHeaderFooterService? _headerFooter;
    private readonly IPdfCropService? _crop;
    private readonly ILogger<BatchJobRunner> _logger;

    public BatchJobRunner(
        ILogger<BatchJobRunner> logger,
        IWatermarkService? watermark = null,
        IHeaderFooterService? headerFooter = null,
        IPdfCropService? crop = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _watermark = watermark;
        _headerFooter = headerFooter;
        _crop = crop;
    }

    public async Task<IReadOnlyList<BatchJobResult>> RunAsync(
        IReadOnlyList<BatchJob> jobs,
        int maxParallelism,
        IProgress<BatchProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        if (jobs.Count == 0)
        {
            return [];
        }

        // Already-cancelled token: short-circuit so we don't even call into Parallel.
        if (ct.IsCancellationRequested)
        {
            int completed = 0;
            var cancelled = new List<BatchJobResult>(jobs.Count);
            foreach (var job in jobs)
            {
                var result = new BatchJobResult(job, BatchJobOutcome.Cancelled);
                cancelled.Add(result);
                progress?.Report(new BatchProgress(++completed, jobs.Count, result));
            }
            return cancelled;
        }

        var results = new ConcurrentDictionary<BatchJob, BatchJobResult>(ReferenceEqualityComparer.Instance);
        int done = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, maxParallelism),
            CancellationToken = CancellationToken.None, // we handle cancel per-job below
        };

        await Parallel.ForEachAsync(jobs, options, async (job, _) =>
        {
            BatchJobResult result = await RunOneAsync(job, ct).ConfigureAwait(false);
            results[job] = result;
            int snapshot = Interlocked.Increment(ref done);
            progress?.Report(new BatchProgress(snapshot, jobs.Count, result));
        }).ConfigureAwait(false);

        // Stable order by job position so callers can pair request ↔ response by index.
        return [.. jobs.Select(j => results[j])];
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Per-job isolation: surface as Failed instead of tearing down the batch.")]
    private async Task<BatchJobResult> RunOneAsync(BatchJob job, CancellationToken ct)
    {
        try
        {
            await DispatchAsync(job, ct).ConfigureAwait(false);
            return new BatchJobResult(job, BatchJobOutcome.Succeeded);
        }
        catch (OperationCanceledException)
        {
            return new BatchJobResult(job, BatchJobOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch job failed: {Source} → {Target} ({Op})",
                job.SourcePath, job.TargetPath, job.Operation.GetType().Name);
            return new BatchJobResult(job, BatchJobOutcome.Failed, ex.Message);
        }
    }

    private Task DispatchAsync(BatchJob job, CancellationToken ct) => job.Operation switch
    {
        BatchWatermarkOperation w => _watermark is null
            ? throw new InvalidOperationException("IWatermarkService is not configured.")
            : _watermark.ApplyAsync(job.SourcePath, w.Spec, job.TargetPath, ct),
        BatchHeaderFooterOperation h => _headerFooter is null
            ? throw new InvalidOperationException("IHeaderFooterService is not configured.")
            : _headerFooter.ApplyAsync(job.SourcePath, h.Spec, job.TargetPath, ct),
        BatchCropOperation c => _crop is null
            ? throw new InvalidOperationException("IPdfCropService is not configured.")
            : _crop.ApplyAsync(job.SourcePath, c.Spec, job.TargetPath, ct),
        _ => throw new NotSupportedException($"Unknown batch operation: {job.Operation.GetType().Name}."),
    };
}
