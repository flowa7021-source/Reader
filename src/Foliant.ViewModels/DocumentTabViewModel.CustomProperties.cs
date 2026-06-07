using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Custom document <c>/Info</c> properties («Custom Properties») wired to
/// <see cref="IPdfCustomPropertiesService"/>. These are the non-standard key/value entries in the PDF
/// <c>/Info</c> dictionary — everything beyond the six classic fields that the
/// <c>DocumentPropertiesDialog</c> already edits. The View opens the dialog after
/// <c>LoadCustomProperties</c> has read the current entries into <see cref="CurrentCustomProperties"/>;
/// the dialog returns a <see cref="SaveCustomPropertiesRequest"/> which the View forwards to
/// <c>SaveCustomProperties</c>. Same gate/suppress shape as the page-labels and OCG-layers partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfCustomProperty> _currentCustomProperties = [];

    /// <summary>Snapshot of the custom <c>/Info</c> entries loaded by the most recent
    /// <c>LoadCustomPropertiesCommand</c> execution. Exposed read-only so the «Custom Properties»
    /// dialog can pre-fill its editable key/value list.</summary>
    public IReadOnlyList<PdfCustomProperty> CurrentCustomProperties => _currentCustomProperties;

    /// <summary>Can the current document's custom properties be edited: service present and source is
    /// a PDF. Same gate shape as <see cref="CanEditMetadata"/> / <see cref="CanEditPageLabels"/>.</summary>
    public bool CanEditCustomProperties =>
        _customPropertiesService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read custom <c>/Info</c> entries from the source PDF via
    /// <see cref="IPdfCustomPropertiesService.ListAsync"/> and re-populate
    /// <see cref="CurrentCustomProperties"/>. Best-effort: a missing or corrupt <c>/Info</c>
    /// dictionary leaves the snapshot empty (the dialog then opens with no rows) instead of crashing
    /// the tab.</summary>
    [RelayCommand(CanExecute = nameof(CanEditCustomProperties))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Custom-property read failure must not crash the tab.")]
    private async Task LoadCustomPropertiesAsync(CancellationToken ct)
    {
        if (_customPropertiesService is null || !CanEditCustomProperties)
        {
            return;
        }

        try
        {
            var properties = await _customPropertiesService.ListAsync(_filePath, ct);
            _currentCustomProperties.Clear();
            _currentCustomProperties.AddRange(properties);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read custom properties from '{Path}'.", _filePath);
        }
    }

    /// <summary>Write <paramref name="request"/>'s custom entries into a new PDF (the source is never
    /// mutated, watermark/redact pattern). The service preserves the standard <c>/Info</c> keys and
    /// replaces only the custom set. No-op when the service is absent, the source is not a PDF, or the
    /// request lacks a target path.</summary>
    [RelayCommand(CanExecute = nameof(CanEditCustomProperties))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Custom-property save failure must not crash the tab.")]
    private async Task SaveCustomPropertiesAsync(SaveCustomPropertiesRequest? request, CancellationToken ct)
    {
        if (_customPropertiesService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanEditCustomProperties)
        {
            return;
        }

        try
        {
            await _customPropertiesService.SetAsync(_filePath, request.TargetPath, request.CustomProperties, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save custom properties to '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>SaveCustomPropertiesCommand</c>: the edited custom entries
/// gathered from the «Custom Properties» dialog + target path picked via Save-As. Defined here (not in
/// Domain) because it is a UI-flow concern, mirroring <see cref="SavePageLabelsRequest"/>.</summary>
public sealed record SaveCustomPropertiesRequest(
    IReadOnlyList<PdfCustomProperty> CustomProperties,
    string TargetPath);
