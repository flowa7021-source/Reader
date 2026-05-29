using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>Видимость полосы миниатюр (toggle из меню View).</summary>
    [ObservableProperty]
    private bool _isThumbnailStripVisible;

    [RelayCommand]
    private void ToggleThumbnailStrip() => IsThumbnailStripVisible = !IsThumbnailStripVisible;

    /// <summary>Слоты страниц для multi-page раскладок (continuous/two-page). Пуста в
    /// <see cref="ViewMode.SinglePage"/> (там используется <see cref="CurrentRender"/>).
    /// Каждый слот рендерится лениво по запросу View; смена zoom/темы/режима их пересобирает.</summary>
    public ObservableCollection<RenderedPageViewModel> VisiblePages { get; } = [];

    /// <summary>Привести <see cref="VisiblePages"/> к <see cref="VisiblePageIndices"/>.
    /// <paramref name="forceRerender"/> = true (смена zoom/темы) пересобирает слоты, чтобы
    /// они перерисовались с новыми опциями; при том же наборе индексов иначе — no-op.</summary>
    private void SyncVisiblePages(bool forceRerender)
    {
        if (ViewMode == ViewMode.SinglePage)
        {
            ClearVisiblePages();
            return;
        }

        IReadOnlyList<int> wanted = VisiblePageIndices;
        if (!forceRerender
            && VisiblePages.Count == wanted.Count
            && VisiblePages.Select(p => p.PageIndex).SequenceEqual(wanted))
        {
            return;
        }

        ClearVisiblePages();
        foreach (int index in wanted)
        {
            VisiblePages.Add(new RenderedPageViewModel(
                index,
                (i, opts, ct) => _document.RenderPageAsync(i, opts, ct),
                BuildRenderOptions,
                ComputePageSearchHighlightsAsync)
            {
                Annotations = AnnotationsForPage(index),
            });
        }
    }

    private IReadOnlyList<Annotation> AnnotationsForPage(int pageIndex) =>
        [.. _allAnnotations.Where(a => a.PageIndex == pageIndex)];

    /// <summary>Прямоугольники подсветки поиска для одной страницы по активному запросу.
    /// Пустой запрос → пусто (без загрузки text-layer). Передаётся слотам как provider, поэтому
    /// text-layer грузится лениво — только для реально отрисованных страниц.</summary>
    private async Task<IReadOnlyList<AnnotationRect>> ComputePageSearchHighlightsAsync(int pageIndex, CancellationToken ct)
    {
        string? query = _activeHighlightQuery;
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        TextLayer? layer = await _document.GetTextLayerAsync(pageIndex, ct);
        return SearchHighlight.MatchRects(
            layer ?? TextLayer.Empty(pageIndex), query,
            _activeHighlightMatchCase, _activeHighlightWholeWord, _activeHighlightFoldDiacritics);
    }

    /// <summary>Обновить снимки аннотаций видимых страниц (multi-page) после add/remove/update.</summary>
    internal void RefreshVisiblePageAnnotations()
    {
        foreach (RenderedPageViewModel page in VisiblePages)
        {
            page.Annotations = AnnotationsForPage(page.PageIndex);
        }
    }

    /// <summary>Пересчитать подсветку поиска видимых страниц (multi-page) — после нового запроса,
    /// очистки или смены страницы. Каждый слот сам вернёт пусто, если запрос неактивен.</summary>
    internal void RefreshVisiblePageSearchHighlights()
    {
        foreach (RenderedPageViewModel page in VisiblePages)
        {
            _ = page.RefreshHighlightsAsync(CancellationToken.None);
        }
    }

    private void ClearVisiblePages()
    {
        foreach (RenderedPageViewModel page in VisiblePages)
        {
            page.Dispose();
        }
        VisiblePages.Clear();
    }

    partial void OnViewModeChanged(ViewMode value) => SyncVisiblePages(forceRerender: false);

    partial void OnTwoPageCoverSeparateChanged(bool value) => SyncVisiblePages(forceRerender: false);

    partial void OnThemeChanged(RenderTheme value) => SyncVisiblePages(forceRerender: true);
}
