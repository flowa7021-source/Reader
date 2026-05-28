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
}

/// <summary>View-supplied envelope for ApplyWatermarkCommand: spec collected in dialog + target path
/// picked via Save-As. Defined here (not in Domain) because it's a UI-flow concern, not a domain type.</summary>
public sealed record ApplyWatermarkRequest(WatermarkSpec Spec, string TargetPath);

/// <summary>View-supplied envelope for ApplyHeaderFooterCommand: spec + target path.</summary>
public sealed record ApplyHeaderFooterRequest(HeaderFooterSpec Spec, string TargetPath);
