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
    private bool _activeHighlightWholeWord;
    private int _searchHighlightGeneration;

    /// <summary>Прямоугольники (PDF-точки) строк текущей страницы, содержащих активный
    /// поисковый запрос — для подсветки поверх рендера (overlay биндится как и аннотации).</summary>
    public ObservableCollection<AnnotationRect> CurrentPageSearchHighlights { get; } = [];

    private void ClearSearchHighlights()
    {
        Interlocked.Increment(ref _searchHighlightGeneration); // supersede any in-flight refresh
        _activeHighlightQuery = null;
        if (CurrentPageSearchHighlights.Count > 0)
        {
            CurrentPageSearchHighlights.Clear();
        }
        RefreshVisiblePageSearchHighlights(); // multi-page slots clear themselves (no active query)
    }

    /// <summary>Пересчитать подсветку поиска для текущей страницы по активному запросу.
    /// Вызывается после поиска и при смене страницы; без активного запроса — очищает.
    /// Поколение отбрасывает устаревший результат при быстрой смене страниц.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Highlight refresh runs fire-and-forget on page change; failures are logged, never crash the tab.")]
    internal async Task RefreshSearchHighlightsAsync(CancellationToken ct)
    {
        int generation = Interlocked.Increment(ref _searchHighlightGeneration);
        string? query = _activeHighlightQuery;
        int pageIndex = CurrentPageIndex;

        if (ViewMode != ViewMode.SinglePage)
        {
            // Multi-page: каждая видимая страница считает свою подсветку лениво (по реализации
            // и здесь — при смене запроса/страницы). Одностраничный overlay скрыт, не трогаем.
            RefreshVisiblePageSearchHighlights();
        }

        if (string.IsNullOrEmpty(query))
        {
            CurrentPageSearchHighlights.Clear();
            return;
        }

        try
        {
            TextLayer? layer = await _document.GetTextLayerAsync(pageIndex, ct);
            if (generation != Volatile.Read(ref _searchHighlightGeneration))
            {
                return; // a newer refresh (page flip / clear) superseded this one
            }

            IReadOnlyList<AnnotationRect> rects = SearchHighlight.MatchRects(
                layer ?? TextLayer.Empty(pageIndex), query, _activeHighlightMatchCase, _activeHighlightWholeWord);

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
            _logger.LogWarning(ex, "Search-highlight refresh failed on page {Page} of '{Title}'.", pageIndex, Title);
        }
    }
}
