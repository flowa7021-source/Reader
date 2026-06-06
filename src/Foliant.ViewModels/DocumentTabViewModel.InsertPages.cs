using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// «Insert Pages from PDF» command wired to <see cref="IPdfInsertPagesService"/>. The View collects
/// the insert position + the PDF to insert + a Save-As target and forwards them to
/// <see cref="InsertPagesCommand"/> — same one-shot shape as the merge / watermark commands. Produces
/// a new PDF; the source is never mutated.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Can pages be inserted into the current document: service present and source is a PDF.</summary>
    public bool CanInsertPages =>
        _insertPagesService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Insert all pages of <see cref="InsertPagesRequest.PdfToInsertPath"/> after
    /// <see cref="InsertPagesRequest.InsertAfterPageIndex"/> (0-based; -1 = before the first page) and
    /// save the result to <see cref="InsertPagesRequest.TargetPath"/>. No-op when the service is
    /// absent, the source is not a PDF, or required paths are blank.</summary>
    [RelayCommand(CanExecute = nameof(CanInsertPages))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Insert-pages failure must not crash the tab.")]
    private async Task InsertPagesAsync(InsertPagesRequest? request, CancellationToken ct)
    {
        if (_insertPagesService is null
            || request is null
            || string.IsNullOrWhiteSpace(request.PdfToInsertPath)
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanInsertPages)
        {
            return;
        }

        try
        {
            await _insertPagesService.InsertAsync(_filePath, request.InsertAfterPageIndex, request.PdfToInsertPath, request.TargetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to insert pages into '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>InsertPagesCommand</c>: 0-based insert position
/// (-1 = before the first page), the PDF whose pages are inserted, and the Save-As target. UI-flow
/// concern, mirroring <see cref="SavePageLabelsRequest"/>.</summary>
public sealed record InsertPagesRequest(int InsertAfterPageIndex, string PdfToInsertPath, string TargetPath);
