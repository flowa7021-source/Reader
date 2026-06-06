using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Named-destination commands wired to <see cref="IPdfNamedDestinationService"/>. The View opens the
/// dialog after <c>LoadNamedDestinations</c> has read the list into <see cref="CurrentNamedDestinations"/>;
/// the dialog returns a single action (Add / Remove) which the View forwards with a Save-As target.
/// Add/Remove produce a new PDF (original never mutated). Same gate/suppress shape as the attachment
/// partial.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private readonly List<PdfNamedDestination> _currentNamedDestinations = [];

    /// <summary>Snapshot of named destinations loaded by the most recent <c>LoadNamedDestinationsCommand</c>
    /// execution. Read-only; the dialog lists it and pre-selects for Remove.</summary>
    public IReadOnlyList<PdfNamedDestination> CurrentNamedDestinations => _currentNamedDestinations;

    /// <summary>Can the current document's named destinations be managed: service present and source is
    /// a PDF.</summary>
    public bool CanManageNamedDestinations =>
        _namedDestinationService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read named destinations via <see cref="IPdfNamedDestinationService.ListAsync"/> into
    /// <see cref="CurrentNamedDestinations"/>. Best-effort.</summary>
    [RelayCommand(CanExecute = nameof(CanManageNamedDestinations))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Named-destination list failure must not crash the tab.")]
    private async Task LoadNamedDestinationsAsync(CancellationToken ct)
    {
        if (_namedDestinationService is null || !CanManageNamedDestinations)
        {
            return;
        }

        try
        {
            var items = await _namedDestinationService.ListAsync(_filePath, ct);
            _currentNamedDestinations.Clear();
            _currentNamedDestinations.AddRange(items);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list named destinations of '{Path}'.", _filePath);
        }
    }

    /// <summary>Add/replace a named destination in a new PDF. No-op on missing service / blank name or
    /// target.</summary>
    [RelayCommand(CanExecute = nameof(CanManageNamedDestinations))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Named-destination add failure must not crash the tab.")]
    private async Task AddNamedDestinationAsync(AddNamedDestinationRequest? request, CancellationToken ct)
    {
        if (_namedDestinationService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanManageNamedDestinations)
        {
            return;
        }

        try
        {
            await _namedDestinationService.AddAsync(_filePath, request.TargetPath, request.Name, request.PageIndex, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add named destination to '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Remove a named destination in a new PDF. No-op on missing service / blank name or
    /// target.</summary>
    [RelayCommand(CanExecute = nameof(CanManageNamedDestinations))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Named-destination remove failure must not crash the tab.")]
    private async Task RemoveNamedDestinationAsync(RemoveNamedDestinationRequest? request, CancellationToken ct)
    {
        if (_namedDestinationService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanManageNamedDestinations)
        {
            return;
        }

        try
        {
            await _namedDestinationService.RemoveAsync(_filePath, request.TargetPath, request.Name, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove named destination from '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>AddNamedDestinationCommand</c>: name + 0-based target page +
/// Save-As target. UI-flow concern, mirroring <see cref="AddAttachmentRequest"/>.</summary>
public sealed record AddNamedDestinationRequest(string Name, int PageIndex, string TargetPath);

/// <summary>View-supplied envelope for <c>RemoveNamedDestinationCommand</c>: name + Save-As target.</summary>
public sealed record RemoveNamedDestinationRequest(string Name, string TargetPath);
