using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Domain;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    private double _viewportWidthPx;
    private double _viewportHeightPx;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisiblePageIndices))]
    [NotifyPropertyChangedFor(nameof(IsSinglePageView))]
    [NotifyPropertyChangedFor(nameof(IsContinuousView))]
    [NotifyPropertyChangedFor(nameof(IsTwoPageView))]
    private ViewMode _viewMode = ViewMode.SinglePage;

    [ObservableProperty]
    private FitMode _fitMode = FitMode.ActualSize;

    /// <summary>True в одностраничном режиме (привязка видимости одностраничной поверхности).</summary>
    public bool IsSinglePageView => ViewMode == ViewMode.SinglePage;

    /// <summary>True в непрерывном режиме (привязка видимости виртуализованной ленты).</summary>
    public bool IsContinuousView => ViewMode == ViewMode.Continuous;

    /// <summary>True в режиме разворота (привязка видимости горизонтальной пары).</summary>
    public bool IsTwoPageView => ViewMode == ViewMode.TwoPage;

    /// <summary>Индексы страниц, которые показывает область просмотра при текущем
    /// <see cref="ViewMode"/>: одна страница, разворот из двух или все (непрерывная лента).</summary>
    public IReadOnlyList<int> VisiblePageIndices
    {
        get
        {
            if (PageCount <= 0)
            {
                return [];
            }
            return ViewMode switch
            {
                ViewMode.TwoPage => CurrentPageIndex + 1 < PageCount
                    ? [CurrentPageIndex, CurrentPageIndex + 1]
                    : [CurrentPageIndex],
                ViewMode.Continuous => [.. Enumerable.Range(0, PageCount)],
                _ => [CurrentPageIndex],
            };
        }
    }

    /// <summary>Сообщить VM размер области просмотра (px); вызывается View при ресайзе.
    /// Если активна подгонка (<see cref="FitMode"/> ≠ ActualSize) — масштаб пересчитывается.</summary>
    public void SetViewport(double widthPx, double heightPx)
    {
        _viewportWidthPx = widthPx;
        _viewportHeightPx = heightPx;
        ApplyFitIfNeeded();
    }

    partial void OnFitModeChanged(FitMode value) => ApplyFitIfNeeded();

    /// <summary>Пересчитать <see cref="Zoom"/> под область просмотра, если включена подгонка.
    /// Вызывается при смене страницы/режима/размера окна.</summary>
    internal void ApplyFitIfNeeded()
    {
        if (FitMode == FitMode.ActualSize)
        {
            return;
        }

        PageSize page;
        try
        {
            page = _document.GetPageSize(CurrentPageIndex);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        double? zoom = FitZoomCalculator.Compute(
            FitMode, page, _viewportWidthPx, _viewportHeightPx, MinZoom, MaxZoom);
        if (zoom is { } z)
        {
            Zoom = Math.Round(z, 2);
        }
    }

    [RelayCommand]
    private void SetSinglePageView() => ViewMode = ViewMode.SinglePage;

    [RelayCommand]
    private void SetContinuousView() => ViewMode = ViewMode.Continuous;

    [RelayCommand]
    private void SetTwoPageView() => ViewMode = ViewMode.TwoPage;

    [RelayCommand]
    private void FitWidth() => FitMode = FitMode.FitWidth;

    [RelayCommand]
    private void FitPage() => FitMode = FitMode.FitPage;

    [RelayCommand]
    private void ActualSize()
    {
        FitMode = FitMode.ActualSize;
        Zoom = 1.0;
    }
}
