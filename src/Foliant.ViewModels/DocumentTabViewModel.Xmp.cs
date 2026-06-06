using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// XMP metadata commands wired to <see cref="IPdfXmpService"/>. The View opens the dialog after
/// <c>LoadXmp</c> has read the current packet into <see cref="CurrentXmp"/>; the dialog returns the
/// edited packet + a Save-As target which the View forwards to <c>SaveXmp</c>. Write produces a new
/// PDF (original never mutated). Same gate/suppress shape as the page-label / attachment partials.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    private string? _currentXmp;

    /// <summary>The XMP packet loaded by the most recent <c>LoadXmpCommand</c> execution (or
    /// <see langword="null"/> when the document has none). Exposed read-only so the dialog can
    /// pre-fill its editor.</summary>
    public string? CurrentXmp => _currentXmp;

    /// <summary>Can the current document's XMP be edited: service present and source is a PDF.</summary>
    public bool CanEditXmp =>
        _xmpService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read the XMP packet via <see cref="IPdfXmpService.ReadAsync"/> into
    /// <see cref="CurrentXmp"/>. Best-effort: a missing/corrupt <c>/Metadata</c> leaves it null.</summary>
    [RelayCommand(CanExecute = nameof(CanEditXmp))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "XMP read failure must not crash the tab.")]
    private async Task LoadXmpAsync(CancellationToken ct)
    {
        if (_xmpService is null || !CanEditXmp)
        {
            return;
        }

        try
        {
            _currentXmp = await _xmpService.ReadAsync(_filePath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read XMP metadata from '{Path}'.", _filePath);
        }
    }

    /// <summary>Write <see cref="SaveXmpRequest.Packet"/> into a new PDF at
    /// <see cref="SaveXmpRequest.TargetPath"/>. No-op when the service is absent, the source is not a
    /// PDF, or the target path is blank.</summary>
    [RelayCommand(CanExecute = nameof(CanEditXmp))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "XMP write failure must not crash the tab.")]
    private async Task SaveXmpAsync(SaveXmpRequest? request, CancellationToken ct)
    {
        if (_xmpService is null
            || request is null
            || request.Packet is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanEditXmp)
        {
            return;
        }

        try
        {
            await _xmpService.WriteAsync(_filePath, request.TargetPath, request.Packet, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save XMP metadata to '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>SaveXmpCommand</c>: the edited XMP packet + Save-As target.
/// UI-flow concern, mirroring <see cref="SavePageLabelsRequest"/>.</summary>
public sealed record SaveXmpRequest(string Packet, string TargetPath);
