using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// VM одной миниатюры страницы в полосе превью. Рисует своё изображение лениво по
/// запросу View (при реализации элемента) и кэширует его: миниатюра отражает СОДЕРЖИМОЕ
/// страницы, поэтому переживает <c>Pages.Move</c> без переотрисовки. <see cref="PageIndex"/>
/// отражает ТЕКУЩУЮ позицию в полосе (меняется после reorder).
/// </summary>
public sealed partial class PageThumbnailViewModel : ObservableObject, IDisposable
{
    private readonly Func<int, CancellationToken, Task<IPageRender>>? _renderThumbnail;
    private int _generation;
    private bool _disposed;

    /// <summary>Текущая 0-based позиция страницы в полосе превью.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>True, если эта страница сейчас выделена в полосе.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>1-based номер страницы, отображаемый пользователю.</summary>
    [ObservableProperty]
    private int _displayNumber;

    /// <summary>Отрисованная миниатюра (маленький рендер) или null, пока не запрошена.</summary>
    [ObservableProperty]
    private IPageRender? _thumbnail;

    /// <summary>Создаёт VM миниатюры для страницы с указанной 0-based позицией.</summary>
    /// <param name="pageIndex">0-based позиция страницы в полосе превью.</param>
    /// <param name="renderThumbnail">Колбэк ленивой отрисовки миниатюры по индексу; null —
    /// изображение не рисуется (полоса показывает только номера).</param>
    public PageThumbnailViewModel(
        int pageIndex,
        Func<int, CancellationToken, Task<IPageRender>>? renderThumbnail = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        PageIndex = pageIndex;
        DisplayNumber = pageIndex + 1;
        _renderThumbnail = renderThumbnail;
    }

    /// <summary>Отрисовать миниатюру, если ещё не отрисована (идемпотентно). Вызывается View
    /// при реализации элемента полосы.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Thumbnail render failure must leave the slot blank, not crash the strip.")]
    public async Task EnsureThumbnailAsync(CancellationToken ct)
    {
        if (_renderThumbnail is null || Thumbnail is not null || _disposed)
        {
            return;
        }

        int generation = Interlocked.Increment(ref _generation);
        try
        {
            IPageRender result = await _renderThumbnail(PageIndex, ct);
            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                result.Dispose();
                return;
            }
            Thumbnail = result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error.
        }
        catch (Exception)
        {
            Thumbnail = null; // leave the slot blank
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _generation);
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}

/// <summary>
/// VM полосы миниатюр страниц: управляет порядком и выделением, делегируя
/// фактический reorder и переход на страницу инжектированным колбэкам. Полностью
/// независим от WPF и нативного рендеринга — изображения остаются заботой View.
/// </summary>
public sealed partial class ThumbnailStripViewModel : ObservableObject, IDisposable
{
    private readonly Func<int, int, CancellationToken, Task> _reorderAsync;
    private readonly Action<int> _onSelect;
    private readonly Func<int, CancellationToken, Task<IPageRender>>? _renderThumbnail;

    /// <summary>Коллекция миниатюр в текущем порядке отображения.</summary>
    public ObservableCollection<PageThumbnailViewModel> Pages { get; } = [];

    /// <summary>0-based индекс выделенной страницы; запись обновляет выделение и зовёт onSelect.</summary>
    [ObservableProperty]
    private int _selectedPageIndex;

    /// <summary>Создаёт полосу миниатюр на заданное число страниц.</summary>
    /// <param name="pageCount">Количество страниц (≥ 0).</param>
    /// <param name="reorderAsync">Колбэк фактического перемещения страницы from→to.</param>
    /// <param name="onSelect">Колбэк перехода на выделенную 0-based страницу.</param>
    /// <param name="renderThumbnail">Колбэк ленивой отрисовки миниатюры (null — только номера).</param>
    public ThumbnailStripViewModel(
        int pageCount,
        Func<int, int, CancellationToken, Task> reorderAsync,
        Action<int> onSelect,
        Func<int, CancellationToken, Task<IPageRender>>? renderThumbnail = null)
    {
        ArgumentNullException.ThrowIfNull(reorderAsync);
        ArgumentNullException.ThrowIfNull(onSelect);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        _reorderAsync = reorderAsync;
        _onSelect = onSelect;
        _renderThumbnail = renderThumbnail;
        BuildPages(pageCount);
    }

    public void Dispose()
    {
        foreach (PageThumbnailViewModel page in Pages)
        {
            page.Dispose();
        }
    }

    /// <summary>
    /// Перемещает страницу с позиции <paramref name="from"/> на <paramref name="to"/>:
    /// зовёт reorderAsync, переставляет элемент в <see cref="Pages"/> и перенумеровывает
    /// все страницы 0..n-1. No-op при from==to или выходе за границы.
    /// </summary>
    /// <param name="from">0-based исходная позиция.</param>
    /// <param name="to">0-based целевая позиция.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task MoveAsync(int from, int to, CancellationToken ct)
    {
        if (from == to || !IsInRange(from) || !IsInRange(to))
        {
            return;
        }

        await _reorderAsync(from, to, ct);

        bool movedSelected = from == SelectedPageIndex;
        Pages.Move(from, to);
        Renumber();

        if (movedSelected)
        {
            SelectedPageIndex = to;
        }
    }

    /// <summary>
    /// Пересобирает <see cref="Pages"/> под новое число страниц (после add/delete)
    /// и зажимает <see cref="SelectedPageIndex"/> в допустимый диапазон.
    /// </summary>
    /// <param name="pageCount">Новое количество страниц (≥ 0).</param>
    public void SetPageCount(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        BuildPages(pageCount);
        int clamped = pageCount == 0 ? 0 : Math.Clamp(SelectedPageIndex, 0, pageCount - 1);
        if (clamped == SelectedPageIndex)
        {
            ApplySelection(clamped);
        }
        else
        {
            SelectedPageIndex = clamped;
        }
    }

    /// <summary>Генерируемый хук: реагирует на смену <see cref="SelectedPageIndex"/>.</summary>
    /// <param name="value">Новое значение индекса.</param>
    partial void OnSelectedPageIndexChanged(int value)
    {
        ApplySelection(value);
        if (IsInRange(value))
        {
            _onSelect(value);
        }
    }

    private void ApplySelection(int value)
    {
        foreach (PageThumbnailViewModel page in Pages)
        {
            page.IsSelected = page.PageIndex == value;
        }
    }

    private void BuildPages(int pageCount)
    {
        foreach (PageThumbnailViewModel page in Pages)
        {
            page.Dispose();
        }
        Pages.Clear();
        for (int i = 0; i < pageCount; i++)
        {
            Pages.Add(new PageThumbnailViewModel(i, _renderThumbnail));
        }
    }

    private void Renumber()
    {
        for (int i = 0; i < Pages.Count; i++)
        {
            Pages[i].PageIndex = i;
            Pages[i].DisplayNumber = i + 1;
        }
    }

    private bool IsInRange(int index) => index >= 0 && index < Pages.Count;
}
