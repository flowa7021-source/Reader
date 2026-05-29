using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// VM for the batch-processing window (Q-F29 UI). Wraps the cross-platform
/// <see cref="IBatchJobRunner"/> with: collection of input files, a chosen operation
/// (watermark / header-footer / crop), the spec for that operation (captured by the View
/// through the existing dialogs), and an output folder. Progress is reported back through
/// <see cref="IProgress{T}"/> which captures the calling synchronization context — when
/// constructed on the UI thread, the View receives marshalled updates automatically.
/// </summary>
public sealed partial class BatchViewModel : ObservableObject, IDisposable
{
    private readonly IBatchJobRunner _runner;
    private readonly ILogger<BatchViewModel> _logger;

    public BatchViewModel(IBatchJobRunner runner, ILogger<BatchViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);
        _runner = runner;
        _logger = logger;

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(CanRun));
            RunBatchCommand.NotifyCanExecuteChanged();
            ClearFilesCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>Input files to process. Each item is a row with a status column for the View.</summary>
    public ObservableCollection<BatchFileRow> Files { get; } = [];

    public int FileCount => Files.Count;

    /// <summary>Operation type. Changing it should clear <see cref="CurrentSpec"/> in the View
    /// because the previously-collected spec belongs to a different operation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(RunBatchCommand))]
    private BatchOperationKind _operation = BatchOperationKind.Watermark;

    /// <summary>The spec the View collected via the corresponding dialog. Concrete type is one of
    /// <see cref="WatermarkSpec"/> / <see cref="HeaderFooterSpec"/> / <see cref="CropSpec"/> —
    /// must match <see cref="Operation"/>. <c>null</c> means «not configured yet».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(RunBatchCommand))]
    private object? _currentSpec;

    /// <summary>Where to write the output files. Each input <c>foo.pdf</c> produces
    /// <c>OutputFolder/foo-&lt;suffix&gt;.pdf</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(RunBatchCommand))]
    private string? _outputFolder;

    /// <summary>Bounded concurrency for the runner. PDFium's <c>NativeGate</c> serialises
    /// per-document anyway, so this mostly bounds memory pressure from many open documents.</summary>
    [ObservableProperty]
    private int _maxParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(RunBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _totalCount;

    private CancellationTokenSource? _cts;

    /// <summary>True when all conditions for kicking off a batch are met: not already running,
    /// at least one input file, a spec collected, and an output folder picked.</summary>
    public bool CanRun =>
        !IsRunning
        && Files.Count > 0
        && CurrentSpec is not null
        && !string.IsNullOrWhiteSpace(OutputFolder);

    /// <summary>Add one or more file paths to the input list, skipping duplicates by path.</summary>
    [RelayCommand]
    private void AddFiles(IEnumerable<string>? paths)
    {
        if (paths is null)
        {
            return;
        }

        var existing = Files.Select(f => f.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || existing.Contains(p))
            {
                continue;
            }
            Files.Add(new BatchFileRow(p));
            existing.Add(p);
        }
    }

    [RelayCommand]
    private void RemoveFile(BatchFileRow? row)
    {
        if (row is not null)
        {
            Files.Remove(row);
        }
    }

    [RelayCommand(CanExecute = nameof(HasFiles))]
    private void ClearFiles() => Files.Clear();

    private bool HasFiles => Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRun))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Batch failure must not crash the window.")]
    private async Task RunBatchAsync()
    {
        if (!CanRun)
        {
            return;
        }

        List<BatchJob> jobs;
        try
        {
            jobs = BuildJobs();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot build batch jobs (spec/operation mismatch?).");
            return;
        }

        IsRunning = true;
        TotalCount = jobs.Count;
        CompletedCount = 0;
        foreach (var row in Files)
        {
            row.Outcome = null;
            row.Error = null;
        }

        _cts = new CancellationTokenSource();
        var progress = new Progress<BatchProgress>(OnProgress);

        try
        {
            var results = await _runner.RunAsync(jobs, MaxParallelism, progress, _cts.Token).ConfigureAwait(true);
            ApplyResults(results);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — leave whatever progress reported so far
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch run failed.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void CancelBatch() => _cts?.Cancel();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private List<BatchJob> BuildJobs()
    {
        BatchOperation op = (Operation, CurrentSpec) switch
        {
            (BatchOperationKind.Watermark, WatermarkSpec w) => new BatchWatermarkOperation(w),
            (BatchOperationKind.HeaderFooter, HeaderFooterSpec h) => new BatchHeaderFooterOperation(h),
            (BatchOperationKind.Crop, CropSpec c) => new BatchCropOperation(c),
            _ => throw new InvalidOperationException(
                $"CurrentSpec ({CurrentSpec?.GetType().Name ?? "null"}) does not match Operation ({Operation})."),
        };

        string folder = OutputFolder!;
        string suffix = SuffixFor(Operation);

        var jobs = new List<BatchJob>(Files.Count);
        foreach (var row in Files)
        {
            string outName = Path.GetFileNameWithoutExtension(row.SourcePath) + suffix + ".pdf";
            jobs.Add(new BatchJob(row.SourcePath, op, Path.Combine(folder, outName)));
        }
        return jobs;
    }

    private static string SuffixFor(BatchOperationKind kind) => kind switch
    {
        BatchOperationKind.Watermark => "-watermarked",
        BatchOperationKind.HeaderFooter => "-headerfooter",
        BatchOperationKind.Crop => "-cropped",
        _ => "-out",
    };

    private void OnProgress(BatchProgress p)
    {
        CompletedCount = p.Completed;
        BatchFileRow? row = Files.FirstOrDefault(f =>
            string.Equals(f.SourcePath, p.LastResult.Job.SourcePath, StringComparison.Ordinal));
        if (row is not null)
        {
            row.Outcome = p.LastResult.Outcome;
            row.Error = p.LastResult.Error;
        }
    }

    private void ApplyResults(IReadOnlyList<BatchJobResult> results)
    {
        // Idempotent with OnProgress — call sites in tests that bypass Progress<T> still get
        // the final state. Pair by reference (Job is the same record instance).
        foreach (var r in results)
        {
            var row = Files.FirstOrDefault(f =>
                string.Equals(f.SourcePath, r.Job.SourcePath, StringComparison.Ordinal));
            if (row is not null)
            {
                row.Outcome = r.Outcome;
                row.Error = r.Error;
            }
        }
    }
}

/// <summary>Тип операции для batch-UI. Параллель к <see cref="BatchOperation"/>-leaf-record'ам,
/// но без спеки — спека приходит отдельно через диалог View.</summary>
public enum BatchOperationKind
{
    Watermark,
    HeaderFooter,
    Crop,
}

/// <summary>Одна строка в списке файлов batch'а: путь + статус заполняется по мере выполнения.</summary>
public sealed partial class BatchFileRow : ObservableObject
{
    public BatchFileRow(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }

    public string FileName => Path.GetFileName(SourcePath);

    [ObservableProperty]
    private BatchJobOutcome? _outcome;

    [ObservableProperty]
    private string? _error;
}
