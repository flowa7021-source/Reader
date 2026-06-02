using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// PDF-mutate commands wired to <see cref="IWatermarkService"/> and <see cref="IHeaderFooterService"/>.
/// The View collects the spec (via dialog) and a target path (via Save-As) and forwards them
/// to the corresponding command — same pattern as Annotated-PDF export.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Can the current document have a watermark applied: service present and source is a PDF.</summary>
    public bool CanAddWatermark =>
        _watermarkService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Can the current document have a header/footer applied: service present and source is a PDF.</summary>
    public bool CanAddHeaderFooter =>
        _headerFooterService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Can the current document be cropped: service present and source is a PDF.</summary>
    public bool CanCropPages =>
        _cropService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Can the current document have regions redacted: at least one redaction service
    /// is wired (coordinate <see cref="IRedactionService"/> or its find-and-redact wrapper) and
    /// the source is a PDF. Both UI entry points (coordinate API + find-and-redact dialog) gate
    /// on this single property so menu visibility stays in sync.</summary>
    public bool CanRedactPages =>
        (_redactionService is not null || _findAndRedactService is not null)
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Apply <paramref name="request"/>'s watermark spec to the source PDF and write the
    /// result to <see cref="ApplyWatermarkRequest.TargetPath"/>. View supplies both pieces; we don't
    /// guess paths or specs. No-op when the service is absent or the source is not a PDF.</summary>
    [RelayCommand(CanExecute = nameof(CanAddWatermark))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Watermark failure must not crash the tab.")]
    private async Task ApplyWatermarkAsync(ApplyWatermarkRequest? request, CancellationToken ct)
    {
        if (_watermarkService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanAddWatermark)
        {
            return;
        }

        try
        {
            await _watermarkService.ApplyAsync(_filePath, request.Spec, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply watermark to '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Apply <paramref name="request"/>'s header/footer spec to the source PDF and write
    /// the result to <see cref="ApplyHeaderFooterRequest.TargetPath"/>. No-op when service is
    /// absent or source is not a PDF.</summary>
    [RelayCommand(CanExecute = nameof(CanAddHeaderFooter))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Header/footer failure must not crash the tab.")]
    private async Task ApplyHeaderFooterAsync(ApplyHeaderFooterRequest? request, CancellationToken ct)
    {
        if (_headerFooterService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanAddHeaderFooter)
        {
            return;
        }

        try
        {
            await _headerFooterService.ApplyAsync(_filePath, request.Spec, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply header/footer to '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Apply <paramref name="request"/>'s crop spec to the source PDF (reversible
    /// /CropBox per Q-F15) and write the result to <see cref="CropPagesRequest.TargetPath"/>.
    /// No-op when service is absent, source is not a PDF, or spec has no effect.</summary>
    [RelayCommand(CanExecute = nameof(CanCropPages))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Crop failure must not crash the tab.")]
    private async Task CropPagesAsync(CropPagesRequest? request, CancellationToken ct)
    {
        if (_cropService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanCropPages
            || !request.Spec.HasEffect)
        {
            return;
        }

        try
        {
            await _cropService.ApplyAsync(_filePath, request.Spec, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to crop pages to '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Redact coordinate regions in the source PDF and write the result to
    /// <see cref="RedactPagesRequest.TargetPath"/>. The view supplies both the region list (no
    /// MVP UX for mouse-drawn regions yet — this entry point exists for batch / programmatic
    /// callers) and the target path. No-op when the service is absent, the source is not a PDF,
    /// or the region list is empty.</summary>
    [RelayCommand(CanExecute = nameof(CanRedactPages))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Redaction failure must not crash the tab.")]
    private async Task RedactPagesAsync(RedactPagesRequest? request, CancellationToken ct)
    {
        if (_redactionService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || request.Regions.Count == 0
            || !CanRedactPages)
        {
            return;
        }

        try
        {
            await _redactionService.RedactAsync(_filePath, request.TargetPath, request.Regions, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to redact regions to '{Path}'.", request.TargetPath);
        }
    }

    /// <summary>Find every match of <see cref="FindAndRedactRequest.Query"/> (text or regex per
    /// <see cref="FindAndRedactRequest.Options"/>) and physically redact each one, writing the
    /// result to <see cref="FindAndRedactRequest.TargetPath"/>. No-op when the wrapper service is
    /// absent, the query is blank, the source is not a PDF, or no matches are found (in which case
    /// the wrapper deliberately does not produce an output file — see <see cref="IFindAndRedactService"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanRedactPages))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Find-and-redact failure must not crash the tab.")]
    private async Task FindAndRedactAsync(FindAndRedactRequest? request, CancellationToken ct)
    {
        if (_findAndRedactService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.Query)
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanRedactPages)
        {
            return;
        }

        try
        {
            await _findAndRedactService.RedactMatchesAsync(
                _document, _filePath, request.TargetPath, request.Query, request.Options, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed find-and-redact for '{Query}' → '{Path}'.", request.Query, request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for ApplyWatermarkCommand: spec collected in dialog + target path
/// picked via Save-As. Defined here (not in Domain) because it's a UI-flow concern, not a domain type.</summary>
public sealed record ApplyWatermarkRequest(WatermarkSpec Spec, string TargetPath);

/// <summary>View-supplied envelope for ApplyHeaderFooterCommand: spec + target path.</summary>
public sealed record ApplyHeaderFooterRequest(HeaderFooterSpec Spec, string TargetPath);

/// <summary>View-supplied envelope for CropPagesCommand: spec collected via dialog + target path picked via Save-As.</summary>
public sealed record CropPagesRequest(CropSpec Spec, string TargetPath);

/// <summary>View-supplied envelope for RedactPagesCommand: coordinate regions + target path.
/// Exists so future batch / programmatic callers can wire redaction without going through the
/// find-and-redact dialog; the MVP UI only exposes the latter.</summary>
public sealed record RedactPagesRequest(IReadOnlyList<RedactionRegion> Regions, string TargetPath);

/// <summary>View-supplied envelope for FindAndRedactCommand: search query collected via dialog +
/// matching options (case / whole-word / regex / fold-diacritics) + target path picked via Save-As.</summary>
public sealed record FindAndRedactRequest(string Query, FindAndRedactOptions Options, string TargetPath);
