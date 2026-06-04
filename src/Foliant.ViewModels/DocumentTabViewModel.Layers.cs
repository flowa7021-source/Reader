using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// OCG (Optional Content Groups, «PDF layers») commands wired to <see cref="IPdfOcgService"/>.
/// Q-F8 Phase 2 MVP: flat list of layers + on/off toggle. The View opens the dialog after the
/// <c>ShowLayers</c> command has loaded the layers; the dialog returns a
/// <see cref="SaveLayerVisibilityRequest"/> which the View forwards to <c>SaveLayerVisibility</c>.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly ObservableCollection<PdfLayerViewModel> _currentLayers = new();

    /// <summary>Snapshot of the layers loaded by the most recent <c>ShowLayersCommand</c>
    /// execution. Exposed read-only to the View so the layers dialog can bind to it. Mutating the
    /// per-item <see cref="PdfLayerViewModel.IsVisible"/> is the only way the dialog signals user
    /// intent before pressing Save.</summary>
    public IReadOnlyList<PdfLayerViewModel> CurrentLayers => _currentLayers;

    /// <summary>Can the current document expose layers: OCG service present and source is a PDF.
    /// Mirrors the shape of other PDF-mutate gates (<see cref="CanApplyBates"/> et al.).</summary>
    public bool CanShowLayers =>
        _ocgService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read OCG layers from the source PDF via <see cref="IPdfOcgService.ReadLayersAsync"/>
    /// and re-populate <see cref="CurrentLayers"/>. Suppresses service failures the same way
    /// <c>ApplyBatesAsync</c> does so a corrupt OCProperties dictionary does not crash the tab —
    /// the dialog then opens with an empty list and the View shows the «no layers» hint.</summary>
    [RelayCommand(CanExecute = nameof(CanShowLayers))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Layer-read failure must not crash the tab.")]
    private async Task ShowLayersAsync(CancellationToken ct)
    {
        if (_ocgService is null || !CanShowLayers)
        {
            return;
        }

        try
        {
            var layers = await _ocgService.ReadLayersAsync(_filePath, ct);
            _currentLayers.Clear();
            foreach (var layer in layers)
            {
                _currentLayers.Add(new PdfLayerViewModel(layer));
            }
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read layers from '{Path}'.", _filePath);
        }
    }

    /// <summary>Persist the per-layer visibility map from <paramref name="request"/> to a new file
    /// (the source is never mutated, watermark/redact pattern). Forwards the dictionary verbatim;
    /// indices unknown to the document are silently ignored per <see cref="IPdfOcgService"/>'s
    /// best-effort contract.</summary>
    [RelayCommand(CanExecute = nameof(CanShowLayers))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Layer-save failure must not crash the tab.")]
    private async Task SaveLayerVisibilityAsync(SaveLayerVisibilityRequest? request, CancellationToken ct)
    {
        if (_ocgService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanShowLayers)
        {
            return;
        }

        try
        {
            await _ocgService.SetLayerVisibilityAsync(_filePath, request.TargetPath, request.VisibilityByIndex, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save layer visibility to '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>SaveLayerVisibilityCommand</c>: per-index visibility map
/// gathered from the dialog's checkbox list + target path picked via Save-As. Defined here (not in
/// Domain) because it is a UI-flow concern, mirroring <see cref="ApplyBatesRequest"/>.</summary>
public sealed record SaveLayerVisibilityRequest(
    IReadOnlyDictionary<int, bool> VisibilityByIndex,
    string TargetPath);
