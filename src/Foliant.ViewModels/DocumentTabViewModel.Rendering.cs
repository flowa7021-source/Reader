using System.Diagnostics.CodeAnalysis;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    public RenderOptions BuildRenderOptions() => new(Zoom, Theme: Theme, RenderAnnotations: true);

    public async Task RenderCurrentPageAsync(CancellationToken ct)
    {
        await RenderCurrentPageCoreAsync(ct);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Render failure must not crash the tab.")]
    private async Task RenderCurrentPageCoreAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            IPageRender result = await _document.RenderPageAsync(CurrentPageIndex, BuildRenderOptions(), ct);
            IPageRender? old = CurrentRender;
            CurrentRender = result;
            old?.Dispose();
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored — cancellation is not an error.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Failed to render page {PageIndex} of '{Title}'.", CurrentPageIndex, Title);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
