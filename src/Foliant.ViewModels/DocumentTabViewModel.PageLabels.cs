using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Page-label («Number Pages») commands wired to <see cref="IPdfPageLabelService"/>. The View opens
/// the dialog after <c>LoadPageLabels</c> has read the current ranges into
/// <see cref="CurrentPageLabels"/>; the dialog returns a <see cref="SavePageLabelsRequest"/> which
/// the View forwards to <c>SavePageLabels</c>. Same gate/suppress shape as the OCG layers partial.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfPageLabelRange> _currentPageLabels = [];

    /// <summary>Snapshot of the page-label ranges loaded by the most recent <c>LoadPageLabelsCommand</c>
    /// execution. Exposed read-only so the «Number Pages» dialog can pre-fill its editable list.</summary>
    public IReadOnlyList<PdfPageLabelRange> CurrentPageLabels => _currentPageLabels;

    /// <summary>Can the current document's page labels be edited: service present and source is a PDF.
    /// Same gate shape as <see cref="CanEditMetadata"/> / <see cref="CanShowLayers"/>.</summary>
    public bool CanEditPageLabels =>
        _pageLabelService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read page-label ranges from the source PDF via <see cref="IPdfPageLabelService.ReadAsync"/>
    /// and re-populate <see cref="CurrentPageLabels"/>. Best-effort: a corrupt <c>/PageLabels</c> tree
    /// leaves the snapshot empty (the dialog then opens with no rows) instead of crashing the tab.</summary>
    [RelayCommand(CanExecute = nameof(CanEditPageLabels))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Page-label read failure must not crash the tab.")]
    private async Task LoadPageLabelsAsync(CancellationToken ct)
    {
        if (_pageLabelService is null || !CanEditPageLabels)
        {
            return;
        }

        try
        {
            var ranges = await _pageLabelService.ReadAsync(_filePath, ct);
            _currentPageLabels.Clear();
            _currentPageLabels.AddRange(ranges);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read page labels from '{Path}'.", _filePath);
        }
    }

    /// <summary>Write <paramref name="request"/>'s ranges into a new PDF (the source is never mutated,
    /// watermark/redact pattern). No-op when the service is absent, the source is not a PDF, or the
    /// request lacks a target path.</summary>
    [RelayCommand(CanExecute = nameof(CanEditPageLabels))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Page-label save failure must not crash the tab.")]
    private async Task SavePageLabelsAsync(SavePageLabelsRequest? request, CancellationToken ct)
    {
        if (_pageLabelService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanEditPageLabels)
        {
            return;
        }

        try
        {
            await _pageLabelService.WriteAsync(_filePath, request.TargetPath, request.Ranges, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save page labels to '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>SavePageLabelsCommand</c>: the edited ranges gathered from
/// the «Number Pages» dialog + target path picked via Save-As. Defined here (not in Domain) because
/// it is a UI-flow concern, mirroring <see cref="SaveLayerVisibilityRequest"/>.</summary>
public sealed record SavePageLabelsRequest(
    IReadOnlyList<PdfPageLabelRange> Ranges,
    string TargetPath);
