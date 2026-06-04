using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Document Properties (classic PDF <c>/Info</c>) editing wired to <see cref="IPdfMetadataEditService"/>.
/// The View opens the dialog pre-filled from <see cref="CurrentMetadata"/>, collects the new spec plus
/// a Save-As target path, and forwards them to <see cref="SaveMetadataCommand"/> — same shape as the
/// watermark / Bates commands in the PdfEffects partial.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Can the current document's metadata be edited: service present and source is a PDF.
    /// Same gate shape as crop/bates — <c>/Info</c> editing is PDF-only.</summary>
    public bool CanEditMetadata =>
        _metadataEditService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read-only snapshot of the open document's metadata, used by the View to pre-fill the
    /// Document Properties dialog (Title/Author/Subject + Created/Modified). Reads straight from the
    /// live <see cref="IDocument"/> — same source the lazy info-panel VM uses.</summary>
    public DocumentMetadata CurrentMetadata => _document.Metadata;

    /// <summary>Write <paramref name="request"/>'s <c>/Info</c> spec to the source PDF and save the
    /// result to <see cref="SaveMetadataRequest.TargetPath"/>. The View supplies both the spec
    /// (collected in the dialog) and the target path (picked via Save-As); we don't guess either.
    /// No-op when the service is absent, the source is not a PDF, or the request is missing a path.</summary>
    [RelayCommand(CanExecute = nameof(CanEditMetadata))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Metadata-edit failure must not crash the tab.")]
    private async Task SaveMetadataAsync(SaveMetadataRequest? request, CancellationToken ct)
    {
        if (_metadataEditService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanEditMetadata)
        {
            return;
        }

        try
        {
            await _metadataEditService.EditAsync(_filePath, request.TargetPath, request.Spec, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save document properties to '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for SaveMetadataCommand: <c>/Info</c> spec collected via the
/// Document Properties dialog + target path picked via Save-As. Defined here (not in Domain) because
/// it's a UI-flow concern, mirroring ApplyWatermarkRequest / ApplyBatesRequest.</summary>
public sealed record SaveMetadataRequest(PdfMetadataSpec Spec, string TargetPath);
