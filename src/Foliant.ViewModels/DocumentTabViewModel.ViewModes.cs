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

    /// <summary>«Обложка отдельно» в режиме разворота (Q-F2): первая страница показывается одна,
    /// далее развороты (1,2),(3,4)… Иначе развороты выровнены с нуля: (0,1),(2,3)…
    /// Влияет только на <see cref="ViewMode.TwoPage"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisiblePageIndices))]
    private bool _twoPageCoverSeparate;

    /// <summary>Right-to-left чтение (Q-F3): в режиме разворота пары идут «справа налево»
    /// (бо́льший индекс — первым). Начальное значение берётся из <c>AppSettings.RightToLeft</c>
    /// при создании VM; runtime-смена настройки требует пере-открытия документа (как Theme/Language).
    /// Команда <c>ToggleRightToLeft</c> переключает только текущую вкладку — не сохраняет в настройки.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisiblePageIndices))]
    private bool _isRightToLeft;

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
                ViewMode.TwoPage => TwoPageSpread(),
                ViewMode.Continuous => [.. Enumerable.Range(0, PageCount)],
                _ => [CurrentPageIndex],
            };
        }
    }

    /// <summary>Разворот, содержащий <see cref="CurrentPageIndex"/>, выровненный по фиксированным
    /// парам (а не «текущая+следующая»). При <see cref="TwoPageCoverSeparate"/> обложка (стр. 0)
    /// показывается одна, а пары начинаются с (1,2). Последняя непарная страница идёт одна.</summary>
    private IReadOnlyList<int> TwoPageSpread()
    {
        int current = CurrentPageIndex;
        if (TwoPageCoverSeparate && current == 0)
        {
            return [0];
        }

        int start = TwoPageCoverSeparate
            ? current - ((current - 1) % 2)  // current ≥ 1: нечётная → она же, чётная → −1
            : current - (current % 2);

        if (start + 1 >= PageCount)
        {
            return [start];
        }
        return IsRightToLeft ? [start + 1, start] : [start, start + 1];
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
    private void ToggleTwoPageCoverSeparate() => TwoPageCoverSeparate = !TwoPageCoverSeparate;

    [RelayCommand]
    private void ToggleRightToLeft() => IsRightToLeft = !IsRightToLeft;

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
