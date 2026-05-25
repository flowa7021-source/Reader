using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    private string? _activeHighlightQuery;
    private bool _activeHighlightMatchCase;

    /// <summary>Прямоугольники (PDF-точки) строк текущей страницы, содержащих активный
    /// поисковый запрос — для подсветки поверх рендера (overlay биндится как и аннотации).</summary>
    public ObservableCollection<AnnotationRect> CurrentPageSearchHighlights { get; } = [];

    private void ClearSearchHighlights()
    {
        _activeHighlightQuery = null;
        if (CurrentPageSearchHighlights.Count > 0)
        {
            CurrentPageSearchHighlights.Clear();
        }
    }

    /// <summary>Пересчитать подсветку поиска для текущей страницы по активному запросу.
    /// Вызывается после поиска и при смене страницы; без активного запроса — очищает.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Highlight refresh runs fire-and-forget on page change; failures are logged, never crash the tab.")]
    internal async Task RefreshSearchHighlightsAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_activeHighlightQuery))
        {
            CurrentPageSearchHighlights.Clear();
            return;
        }

        try
        {
            TextLayer? layer = await _document.GetTextLayerAsync(CurrentPageIndex, ct);
            IReadOnlyList<AnnotationRect> rects = SearchHighlight.MatchRects(
                layer ?? TextLayer.Empty(CurrentPageIndex), _activeHighlightQuery, _activeHighlightMatchCase);

            CurrentPageSearchHighlights.Clear();
            foreach (AnnotationRect rect in rects)
            {
                CurrentPageSearchHighlights.Add(rect);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search-highlight refresh failed on page {Page} of '{Title}'.", CurrentPageIndex, Title);
        }
    }
}
