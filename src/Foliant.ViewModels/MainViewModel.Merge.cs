using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Multi-source PDF merge command. Lives on <see cref="MainViewModel"/> (not per-tab) because
/// the operation has no notion of «current document» — the View collects N source paths via an
/// OpenFileDialog and a target via SaveFileDialog, and forwards both to this command.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>True when a merge service is registered — the menu item is gated by this.</summary>
    public bool CanMergePdfs => _mergeService is not null;

    /// <summary>Merge the source PDFs in the order they appear in <paramref name="request"/>
    /// into a new file at <see cref="MergePdfsRequest.TargetPath"/>. No-op when the service is
    /// absent, the request is invalid (null/blank/too few sources). Failure is logged, not
    /// thrown — the View shows a generic error dialog only for catastrophic errors that escape
    /// here.</summary>
    [RelayCommand(CanExecute = nameof(CanMergePdfs))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Merge failure must not crash the main window.")]
    private async Task MergePdfsAsync(MergePdfsRequest? request, CancellationToken ct)
    {
        if (_mergeService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || request.Sources.Count < 2)
        {
            return;
        }

        try
        {
            await _mergeService.MergeAsync(request.Sources, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to merge {Count} PDFs into '{Path}'.", request.Sources.Count, request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for MergePdfsCommand: ordered source paths + target. Defined
/// here (not in Domain) because it's a UI-flow concern, not a domain type.</summary>
public sealed record MergePdfsRequest(IReadOnlyList<string> Sources, string TargetPath);
