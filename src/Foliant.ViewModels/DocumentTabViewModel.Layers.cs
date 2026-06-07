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

    /// <summary>Can OCG layers be structurally edited (renamed / removed): edit-service present and
    /// source is a PDF. Distinct from <see cref="CanShowLayers"/> because rename/delete go through the
    /// separate <see cref="IPdfOcgEditService"/> (cos incremental write of the <c>/OCProperties</c>
    /// tree) rather than the visibility-only <see cref="IPdfOcgService"/>. In practice both services
    /// are co-registered, so the two gates move together.</summary>
    public bool CanEditLayers =>
        _ocgEditService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rename a single OCG layer to a new title and save the result to a new file (the source
    /// is never mutated, watermark/redact pattern). The layer is identified by its stable
    /// <see cref="PdfLayerViewModel.Index"/>; an index unknown to the document is silently ignored per
    /// <see cref="IPdfOcgEditService"/>'s best-effort contract. No-op when the service is absent, the
    /// source is not a PDF, or the request lacks a target path or new name.</summary>
    [RelayCommand(CanExecute = nameof(CanEditLayers))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Layer-rename failure must not crash the tab.")]
    private async Task RenameLayerAsync(RenameLayerRequest? request, CancellationToken ct)
    {
        if (_ocgEditService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || string.IsNullOrWhiteSpace(request.NewName)
            || !CanEditLayers)
        {
            return;
        }

        try
        {
            await _ocgEditService.RenameAsync(_filePath, request.TargetPath, request.LayerIndex, request.NewName, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename layer in '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Remove a single OCG layer (its <c>/OCG</c> entry plus references from
    /// <c>/OCProperties</c>) and save the result to a new file. The layer is identified by its stable
    /// <see cref="PdfLayerViewModel.Index"/>. Note: removing a layer drops its <c>/OCProperties</c>
    /// membership but leaves any marked content already drawn on the page — the content simply becomes
    /// unconditionally visible, matching how Acrobat treats a deleted OCG. No-op when the service is
    /// absent, the source is not a PDF, or the request lacks a target path.</summary>
    [RelayCommand(CanExecute = nameof(CanEditLayers))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Layer-delete failure must not crash the tab.")]
    private async Task DeleteLayerAsync(DeleteLayerRequest? request, CancellationToken ct)
    {
        if (_ocgEditService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanEditLayers)
        {
            return;
        }

        try
        {
            await _ocgEditService.RemoveAsync(_filePath, request.TargetPath, request.LayerIndex, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete layer in '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>SaveLayerVisibilityCommand</c>: per-index visibility map
/// gathered from the dialog's checkbox list + target path picked via Save-As. Defined here (not in
/// Domain) because it is a UI-flow concern, mirroring <see cref="ApplyBatesRequest"/>.</summary>
public sealed record SaveLayerVisibilityRequest(
    IReadOnlyDictionary<int, bool> VisibilityByIndex,
    string TargetPath);

/// <summary>View-supplied envelope for <c>RenameLayerCommand</c>: the target layer index, its new
/// title gathered from the Layers dialog's inline prompt, and the Save-As target path. Defined here
/// (not in Domain) because it is a UI-flow concern, mirroring <see cref="SaveLayerVisibilityRequest"/>.</summary>
public sealed record RenameLayerRequest(
    int LayerIndex,
    string NewName,
    string TargetPath);

/// <summary>View-supplied envelope for <c>DeleteLayerCommand</c>: the target layer index + the
/// Save-As target path. Defined here (not in Domain) because it is a UI-flow concern.</summary>
public sealed record DeleteLayerRequest(
    int LayerIndex,
    string TargetPath);
