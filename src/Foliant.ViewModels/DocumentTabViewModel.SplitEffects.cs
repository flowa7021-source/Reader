using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Split / page-extract commands wired to <see cref="Foliant.Application.Services.IPdfSplitService"/>.
/// The View collects the spec (pages-per-chunk or a page selection) plus an output location and
/// forwards a request — same envelope pattern as the watermark / crop / bates commands. The
/// <see cref="CanSplitPdf"/> gate lives in the PdfEffects partial alongside the other Can-* gates.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Split the source PDF into chunks of <see cref="SplitEveryRequest.PagesPerChunk"/>
    /// pages each, writing <c>{BaseFileName}-NNN.pdf</c> files into
    /// <see cref="SplitEveryRequest.OutputDirectory"/>. The view supplies the chunk size (dialog),
    /// directory (folder picker) and base name (derived from the document); we don't guess. No-op
    /// when the service is absent, the source is not a PDF, the request is missing required fields,
    /// or the chunk size is non-positive (the service would otherwise throw).</summary>
    [RelayCommand(CanExecute = nameof(CanSplitPdf))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Split failure must not crash the tab.")]
    private async Task SplitEveryAsync(SplitEveryRequest? request, CancellationToken ct)
    {
        if (_splitService is null
            || request is null
            || request.PagesPerChunk < 1
            || string.IsNullOrWhiteSpace(request.OutputDirectory)
            || string.IsNullOrWhiteSpace(request.BaseFileName)
            || !CanSplitPdf)
        {
            return;
        }

        try
        {
            await _splitService.SplitEveryAsync(
                _filePath, request.PagesPerChunk, request.OutputDirectory, request.BaseFileName, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to split '{Path}' into '{Dir}'.", _filePath, request.OutputDirectory);
        }
    }

    /// <summary>Assemble a single PDF from the (possibly non-contiguous) page selection in
    /// <see cref="ExtractSelectionRequest.PageIndices0Based"/>, writing it to
    /// <see cref="ExtractSelectionRequest.TargetPath"/>. The view validates and converts the user's
    /// 1-based range string to 0-based indices before building the request. No-op when the service
    /// is absent, the source is not a PDF, the selection is empty, or the target path is missing.</summary>
    [RelayCommand(CanExecute = nameof(CanSplitPdf))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Extract failure must not crash the tab.")]
    private async Task ExtractSelectionAsync(ExtractSelectionRequest? request, CancellationToken ct)
    {
        if (_splitService is null
            || request is null
            || request.PageIndices0Based.Count == 0
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanSplitPdf)
        {
            return;
        }

        try
        {
            await _splitService.ExtractSelectionAsync(_filePath, request.PageIndices0Based, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract pages from '{Path}' to '{Target}'.", _filePath, request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for SplitEveryCommand: chunk size collected via dialog, output
/// directory picked via folder browser, base file name derived from the document.</summary>
public sealed record SplitEveryRequest(int PagesPerChunk, string OutputDirectory, string BaseFileName);

/// <summary>View-supplied envelope for ExtractSelectionCommand: 0-based page indices parsed from the
/// user's 1-based range string + target path picked via Save-As.</summary>
public sealed record ExtractSelectionRequest(IReadOnlyList<int> PageIndices0Based, string TargetPath);
