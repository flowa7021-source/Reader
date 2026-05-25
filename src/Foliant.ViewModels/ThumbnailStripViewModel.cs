using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Foliant.ViewModels;

/// <summary>
/// VM одной миниатюры страницы в полосе превью. Хранит только состояние порядка
/// и выделения — само изображение миниатюры рисует View. <see cref="PageIndex"/>
/// отражает ТЕКУЩУЮ позицию страницы в полосе (меняется после reorder).
/// </summary>
public sealed partial class PageThumbnailViewModel : ObservableObject
{
    /// <summary>Текущая 0-based позиция страницы в полосе превью.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>True, если эта страница сейчас выделена в полосе.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>1-based номер страницы, отображаемый пользователю.</summary>
    [ObservableProperty]
    private int _displayNumber;

    /// <summary>Создаёт VM миниатюры для страницы с указанной 0-based позицией.</summary>
    /// <param name="pageIndex">0-based позиция страницы в полосе превью.</param>
    public PageThumbnailViewModel(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        PageIndex = pageIndex;
        DisplayNumber = pageIndex + 1;
    }
}

/// <summary>
/// VM полосы миниатюр страниц: управляет порядком и выделением, делегируя
/// фактический reorder и переход на страницу инжектированным колбэкам. Полностью
/// независим от WPF и нативного рендеринга — изображения остаются заботой View.
/// </summary>
public sealed partial class ThumbnailStripViewModel : ObservableObject
{
    private readonly Func<int, int, CancellationToken, Task> _reorderAsync;
    private readonly Action<int> _onSelect;

    /// <summary>Коллекция миниатюр в текущем порядке отображения.</summary>
    public ObservableCollection<PageThumbnailViewModel> Pages { get; } = [];

    /// <summary>0-based индекс выделенной страницы; запись обновляет выделение и зовёт onSelect.</summary>
    [ObservableProperty]
    private int _selectedPageIndex;

    /// <summary>Создаёт полосу миниатюр на заданное число страниц.</summary>
    /// <param name="pageCount">Количество страниц (≥ 0).</param>
    /// <param name="reorderAsync">Колбэк фактического перемещения страницы from→to.</param>
    /// <param name="onSelect">Колбэк перехода на выделенную 0-based страницу.</param>
    public ThumbnailStripViewModel(
        int pageCount,
        Func<int, int, CancellationToken, Task> reorderAsync,
        Action<int> onSelect)
    {
        ArgumentNullException.ThrowIfNull(reorderAsync);
        ArgumentNullException.ThrowIfNull(onSelect);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        _reorderAsync = reorderAsync;
        _onSelect = onSelect;
        BuildPages(pageCount);
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
        Pages.Clear();
        for (int i = 0; i < pageCount; i++)
        {
            Pages.Add(new PageThumbnailViewModel(i));
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
