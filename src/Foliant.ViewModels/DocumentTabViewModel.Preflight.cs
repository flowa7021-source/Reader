using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Preflight / structure-validation command wired to <see cref="IPdfPreflightService"/> (read-only).
/// Composes the existing PDF inspectors into one <see cref="PdfPreflightReport"/> ("is this PDF OK for
/// print/archive/sharing?"). The View opens the dialog after <c>LoadPreflight</c> has read the report
/// into <see cref="CurrentPreflightReport"/>. Same gate/suppress shape as the fonts/links/output-intents
/// partials, but the snapshot is a single report object rather than a list.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>The report produced by the most recent <c>LoadPreflightCommand</c> execution, or
    /// <see langword="null"/> if it has not run / failed. Read-only; the dialog presents it.</summary>
    public PdfPreflightReport? CurrentPreflightReport { get; private set; }

    /// <summary>Can the current document be preflighted: service present and source is a PDF.</summary>
    public bool CanPreflight =>
        _preflightService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Run preflight via <see cref="IPdfPreflightService.PreflightAsync"/> into
    /// <see cref="CurrentPreflightReport"/>. Best-effort: a failure leaves the report null (the View
    /// then shows the «could not analyse» hint) instead of crashing the tab.</summary>
    [RelayCommand(CanExecute = nameof(CanPreflight))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Preflight failure must not crash the tab.")]
    private async Task LoadPreflightAsync(CancellationToken ct)
    {
        if (_preflightService is null || !CanPreflight)
        {
            return;
        }

        try
        {
            CurrentPreflightReport = await _preflightService.PreflightAsync(_filePath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            CurrentPreflightReport = null;
            _logger.LogWarning(ex, "Failed to preflight '{Path}'.", _filePath);
        }
    }
}
