using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Document JavaScript/actions sanitization commands wired to <see cref="IPdfSanitizationService"/>.
/// The View opens the dialog after <c>ScanForJavaScript</c> has loaded the report into
/// <see cref="CurrentSanitizationReport"/>; if the user confirms removal, the View picks a Save-As
/// target and forwards it to <c>RemoveJavaScript</c> (which writes a cleaned copy; original never
/// mutated). Same gate/suppress shape as the page-label / attachment partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private PdfSanitizationReport? _currentSanitizationReport;

    /// <summary>The scan result from the most recent <c>ScanForJavaScriptCommand</c> execution (or
    /// <see langword="null"/> if not scanned yet). Exposed read-only so the dialog can show findings.</summary>
    public PdfSanitizationReport? CurrentSanitizationReport => _currentSanitizationReport;

    /// <summary>Can the current document be sanitized: service present and source is a PDF.</summary>
    public bool CanSanitize =>
        _sanitizationService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Scan for document-level JavaScript/actions via
    /// <see cref="IPdfSanitizationService.ScanAsync"/> into <see cref="CurrentSanitizationReport"/>.
    /// Best-effort: a corrupt catalog leaves an empty report.</summary>
    [RelayCommand(CanExecute = nameof(CanSanitize))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sanitization scan failure must not crash the tab.")]
    private async Task ScanForJavaScriptAsync(CancellationToken ct)
    {
        if (_sanitizationService is null || !CanSanitize)
        {
            return;
        }

        try
        {
            _currentSanitizationReport = await _sanitizationService.ScanAsync(_filePath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan '{Path}' for JavaScript.", _filePath);
        }
    }

    /// <summary>Write a sanitized copy (JS open-action / document scripts / catalog actions removed)
    /// to <see cref="RemoveJavaScriptRequest.TargetPath"/>. No-op on missing service / blank target.</summary>
    [RelayCommand(CanExecute = nameof(CanSanitize))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sanitization write failure must not crash the tab.")]
    private async Task RemoveJavaScriptAsync(RemoveJavaScriptRequest? request, CancellationToken ct)
    {
        if (_sanitizationService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanSanitize)
        {
            return;
        }

        try
        {
            await _sanitizationService.RemoveJavaScriptAndActionsAsync(_filePath, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sanitize '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>RemoveJavaScriptCommand</c>: the Save-As target for the
/// cleaned PDF. UI-flow concern, mirroring <see cref="SavePageLabelsRequest"/>.</summary>
public sealed record RemoveJavaScriptRequest(string TargetPath);
