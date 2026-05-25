using System.Diagnostics.CodeAnalysis;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    private int _renderGeneration;

    public RenderOptions BuildRenderOptions() => new(Zoom, Theme: Theme, RenderAnnotations: true);

    public async Task RenderCurrentPageAsync(CancellationToken ct)
    {
        await RenderCurrentPageCoreAsync(ct);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Render failure must not crash the tab.")]
    private async Task RenderCurrentPageCoreAsync(CancellationToken ct)
    {
        // Renders are fired without a shared cancellation token, so a slow earlier render can
        // complete after a newer one. Stamp each request and discard results that a later
        // request has superseded, otherwise a stale bitmap could overwrite the current page.
        int generation = Interlocked.Increment(ref _renderGeneration);
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            IPageRender result = await _document.RenderPageAsync(CurrentPageIndex, BuildRenderOptions(), ct);
            if (generation != Volatile.Read(ref _renderGeneration))
            {
                result.Dispose();
                return;
            }
            IPageRender? old = CurrentRender;
            CurrentRender = result;
            old?.Dispose();
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored — cancellation is not an error.
        }
        catch (ObjectDisposedException)
        {
            // The document was disposed/reloaded under us (e.g. after an edit); not an error.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Failed to render page {PageIndex} of '{Title}'.", CurrentPageIndex, Title);
        }
        finally
        {
            if (generation == Volatile.Read(ref _renderGeneration))
            {
                IsLoading = false;
            }
        }
    }
}
