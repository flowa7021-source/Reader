using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Initial-view («Viewer Preferences») commands wired to <see cref="IPdfViewerPreferencesService"/>.
/// The View opens the dialog after <c>LoadViewerPreferences</c> has read the current settings into
/// <see cref="CurrentViewerPreferences"/>; the dialog returns a
/// <see cref="SaveViewerPreferencesRequest"/> which the View forwards to <c>SaveViewerPreferences</c>.
/// Same gate/suppress shape as the page-label / OCG partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private PdfViewerPreferences _currentViewerPreferences = PdfViewerPreferences.Default;

    /// <summary>Settings loaded by the most recent <c>LoadViewerPreferencesCommand</c> execution
    /// (defaults to <see cref="PdfViewerPreferences.Default"/>). Exposed read-only so the «Initial
    /// View» dialog can pre-fill its combos and checkboxes.</summary>
    public PdfViewerPreferences CurrentViewerPreferences => _currentViewerPreferences;

    /// <summary>Can the current document's initial view be edited: service present and source is a
    /// PDF. Same gate shape as <see cref="CanEditPageLabels"/>.</summary>
    public bool CanEditViewerPreferences =>
        _viewerPreferencesService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read initial-view settings via <see cref="IPdfViewerPreferencesService.ReadAsync"/>
    /// into <see cref="CurrentViewerPreferences"/>. Best-effort: failures leave the snapshot at its
    /// previous value (the service itself returns <see cref="PdfViewerPreferences.Default"/> on a
    /// missing/corrupt dictionary) instead of crashing the tab.</summary>
    [RelayCommand(CanExecute = nameof(CanEditViewerPreferences))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Viewer-prefs read failure must not crash the tab.")]
    private async Task LoadViewerPreferencesAsync(CancellationToken ct)
    {
        if (_viewerPreferencesService is null || !CanEditViewerPreferences)
        {
            return;
        }

        try
        {
            _currentViewerPreferences = await _viewerPreferencesService.ReadAsync(_filePath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read viewer preferences from '{Path}'.", _filePath);
        }
    }

    /// <summary>Write <paramref name="request"/>'s settings into a new PDF (the source is never
    /// mutated, watermark/redact pattern). No-op when the service is absent, the source is not a PDF,
    /// or the request lacks a target path.</summary>
    [RelayCommand(CanExecute = nameof(CanEditViewerPreferences))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Viewer-prefs save failure must not crash the tab.")]
    private async Task SaveViewerPreferencesAsync(SaveViewerPreferencesRequest? request, CancellationToken ct)
    {
        if (_viewerPreferencesService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanEditViewerPreferences)
        {
            return;
        }

        try
        {
            await _viewerPreferencesService.WriteAsync(_filePath, request.TargetPath, request.Preferences, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save viewer preferences to '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>SaveViewerPreferencesCommand</c>: the chosen initial-view
/// settings + target path picked via Save-As. Defined here (not in Domain) because it is a UI-flow
/// concern, mirroring <see cref="SavePageLabelsRequest"/>.</summary>
public sealed record SaveViewerPreferencesRequest(
    PdfViewerPreferences Preferences,
    string TargetPath);
