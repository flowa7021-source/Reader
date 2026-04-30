using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Минимальный масштаб (10 %).</summary>
    public const double MinZoom = 0.10;

    /// <summary>Максимальный масштаб (800 %).</summary>
    public const double MaxZoom = 8.00;

    /// <summary>Шаг для команд ZoomIn/ZoomOut (25 п.п. — соответствует ZoomBucket-сетке в Domain).</summary>
    public const double ZoomStep = 0.25;

    private readonly IDocument _document;
    private readonly string _filePath;
    private readonly ISearchService _searchService;
    private readonly IAnnotationService _annotationService;
    private readonly IBookmarkService _bookmarkService;
    private readonly ILogger<DocumentTabViewModel> _logger;
    private readonly List<Annotation> _allAnnotations = [];

    /// <summary>Stack страниц, на которых пользователь был раньше (top — самая свежая).</summary>
    private readonly Stack<int> _navBack = new();
    /// <summary>Stack страниц, отменённых через Back (готовы к Forward).</summary>
    private readonly Stack<int> _navForward = new();
    /// <summary>True пока активна команда Go-Back / Go-Forward — чтобы свой собственный пуш в стек
    /// не делать в OnCurrentPageIndexChanged.</summary>
    private bool _navigatingHistory;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    private int _currentPageIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private IPageRender? _currentRender;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomPercent))]
    private double _zoom = 1.0;

    [ObservableProperty]
    private RenderTheme _theme = RenderTheme.Original;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SearchHit? _selectedSearchHit;

    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    public ObservableCollection<Annotation> CurrentPageAnnotations { get; } = [];

    /// <summary>Все закладки документа, отсортированные по PageIndex. Биндится в sidebar.</summary>
    public ObservableCollection<Bookmark> Bookmarks { get; } = [];

    /// <summary>Session-history search-запросов (most-recent first), capped at <see cref="MaxRecentSearches"/>. Не персистится.</summary>
    public ObservableCollection<string> RecentSearches { get; } = [];

    /// <summary>Сколько последних поисков держим в памяти на вкладку.</summary>
    public const int MaxRecentSearches = 10;

    /// <summary>«N/M» — отображается в статус-баре. Локаль-агностичный формат: чисто цифры.</summary>
    public string PageInfo => $"{CurrentPageIndex + 1}/{Math.Max(PageCount, 1)}";

    /// <summary>Текущий масштаб в процентах для статус-бара. Округлён до целого.</summary>
    public int ZoomPercent => (int)Math.Round(Zoom * 100);

    public bool CanGoBack => _navBack.Count > 0;

    public bool CanGoForward => _navForward.Count > 0;

    public DocumentTabViewModel(
        IDocument document,
        string filePath,
        ISearchService searchService,
        IAnnotationService annotationService,
        IBookmarkService bookmarkService,
        ILogger<DocumentTabViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(annotationService);
        ArgumentNullException.ThrowIfNull(bookmarkService);
        ArgumentNullException.ThrowIfNull(logger);

        _document = document;
        _filePath = filePath;
        _searchService = searchService;
        _annotationService = annotationService;
        _bookmarkService = bookmarkService;
        _logger = logger;
        Title = Path.GetFileName(filePath);
        PageCount = document.PageCount;
    }

    partial void OnCurrentPageIndexChanged(int oldValue, int newValue)
    {
        int clamped = Math.Clamp(newValue, 0, Math.Max(0, PageCount - 1));
        if (clamped != newValue)
        {
            CurrentPageIndex = clamped;
            return;
        }

        // Если переход не из Back/Forward команды — это «новая» навигация:
        // запоминаем откуда ушли, ресет forward-стека (как в браузере).
        if (!_navigatingHistory && oldValue != newValue)
        {
            _navBack.Push(oldValue);
            _navForward.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        RefreshCurrentPageAnnotations();
        _ = RenderCurrentPageAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_navBack.Count == 0)
        {
            return;
        }

        int previous = _navBack.Pop();
        _navForward.Push(CurrentPageIndex);
        _navigatingHistory = true;
        try
        {
            CurrentPageIndex = previous;
        }
        finally
        {
            _navigatingHistory = false;
        }
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_navForward.Count == 0)
        {
            return;
        }

        int next = _navForward.Pop();
        _navBack.Push(CurrentPageIndex);
        _navigatingHistory = true;
        try
        {
            CurrentPageIndex = next;
        }
        finally
        {
            _navigatingHistory = false;
        }
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    partial void OnZoomChanged(double value)
    {
        double clamped = Math.Clamp(value, MinZoom, MaxZoom);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            Zoom = clamped;
            return;
        }
        _ = RenderCurrentPageAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPageIndex < PageCount - 1)
        {
            CurrentPageIndex++;
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
        }
    }

    [RelayCommand]
    private void FirstPage() => CurrentPageIndex = 0;

    [RelayCommand]
    private void LastPage() => CurrentPageIndex = Math.Max(0, PageCount - 1);

    /// <summary>Принимает 1-based номер страницы (как у пользователя в UI), переводит в 0-based индекс.</summary>
    [RelayCommand]
    private void GoToPage(int pageNumber)
    {
        CurrentPageIndex = Math.Clamp(pageNumber - 1, 0, Math.Max(0, PageCount - 1));
    }

    [RelayCommand]
    private void ZoomIn()
    {
        Zoom = Math.Min(MaxZoom, Math.Round(Zoom + ZoomStep, 2));
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Zoom = Math.Max(MinZoom, Math.Round(Zoom - ZoomStep, 2));
    }

    [RelayCommand]
    private void ResetZoom() => Zoom = 1.0;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Annotation load failure must not crash the tab.")]
    public async Task LoadAnnotationsAsync(CancellationToken ct)
    {
        try
        {
            var loaded = await _annotationService.ListAsync(_filePath, ct);
            _allAnnotations.Clear();
            _allAnnotations.AddRange(loaded);
            RefreshCurrentPageAnnotations();
        }
        catch (OperationCanceledException)
        {
            // shutdown — игнорируем
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load annotations for '{Path}'.", _filePath);
        }
    }

    public async Task AddHighlightAsync(int pageIndex, AnnotationRect bounds, string colorHex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(colorHex);

        var hl = Annotation.Highlight(pageIndex, bounds, colorHex, DateTimeOffset.UtcNow);
        await _annotationService.AddAsync(_filePath, hl, ct);
        _allAnnotations.Add(hl);
        if (pageIndex == CurrentPageIndex)
        {
            CurrentPageAnnotations.Add(hl);
        }
    }

    public async Task AddNoteAsync(int pageIndex, AnnotationRect bounds, string text, string colorHex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(colorHex);

        var note = Annotation.StickyNote(pageIndex, bounds, text, colorHex, DateTimeOffset.UtcNow);
        await _annotationService.AddAsync(_filePath, note, ct);
        _allAnnotations.Add(note);
        if (pageIndex == CurrentPageIndex)
        {
            CurrentPageAnnotations.Add(note);
        }
    }

    [RelayCommand]
    private async Task RemoveAnnotationAsync(Annotation? annotation)
    {
        if (annotation is null)
        {
            return;
        }

        var removed = await _annotationService.RemoveAsync(_filePath, annotation.Id, CancellationToken.None);
        if (!removed)
        {
            return;
        }

        _allAnnotations.RemoveAll(a => a.Id == annotation.Id);
        for (int i = CurrentPageAnnotations.Count - 1; i >= 0; i--)
        {
            if (CurrentPageAnnotations[i].Id == annotation.Id)
            {
                CurrentPageAnnotations.RemoveAt(i);
            }
        }
    }

    private void RefreshCurrentPageAnnotations()
    {
        CurrentPageAnnotations.Clear();
        foreach (var a in _allAnnotations.Where(x => x.PageIndex == CurrentPageIndex))
        {
            CurrentPageAnnotations.Add(a);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bookmark load failure must not crash the tab.")]
    public async Task LoadBookmarksAsync(CancellationToken ct)
    {
        try
        {
            var loaded = await _bookmarkService.ListAsync(_filePath, ct);
            Bookmarks.Clear();
            foreach (var bm in loaded.OrderBy(b => b.PageIndex))
            {
                Bookmarks.Add(bm);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown — игнорируем
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load bookmarks for '{Path}'.", _filePath);
        }
    }

    /// <summary>Toggle закладки на текущей странице. Label = "Page N" по умолчанию.</summary>
    [RelayCommand]
    private async Task ToggleBookmarkAsync()
    {
        int page = CurrentPageIndex;
        string defaultLabel = $"Page {page + 1}";

        var bm = await _bookmarkService.ToggleAsync(_filePath, page, defaultLabel, CancellationToken.None);
        if (bm is null)
        {
            // удалили — выкидываем по PageIndex (на странице была одна закладка по контракту Toggle).
            for (int i = Bookmarks.Count - 1; i >= 0; i--)
            {
                if (Bookmarks[i].PageIndex == page)
                {
                    Bookmarks.RemoveAt(i);
                }
            }
            return;
        }

        // вставляем сохранив сортировку по PageIndex
        int insertAt = 0;
        while (insertAt < Bookmarks.Count && Bookmarks[insertAt].PageIndex < bm.PageIndex)
        {
            insertAt++;
        }
        Bookmarks.Insert(insertAt, bm);
    }

    [RelayCommand]
    private void JumpToBookmark(Bookmark? bookmark)
    {
        if (bookmark is null)
        {
            return;
        }
        CurrentPageIndex = bookmark.PageIndex;
    }

    /// <summary>
    /// Pull-to-front + cap. Регистро-нечувствительный дедуп: ввод "Cat" и "cat"
    /// считаются одной записью; новый занимает место старого.
    /// </summary>
    private void PromoteRecentSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        for (int i = RecentSearches.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentSearches[i], query, StringComparison.OrdinalIgnoreCase))
            {
                RecentSearches.RemoveAt(i);
            }
        }
        RecentSearches.Insert(0, query);
        while (RecentSearches.Count > MaxRecentSearches)
        {
            RecentSearches.RemoveAt(RecentSearches.Count - 1);
        }
    }

    /// <summary>Команда заполняет <see cref="SearchText"/> из истории и сразу запускает поиск.</summary>
    [RelayCommand]
    private async Task SelectRecentSearchAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }
        SearchText = query;
        await RunSearchAsync();
    }

    /// <summary>Перейти к следующему хиту в <see cref="SearchResults"/>; wrap при достижении конца.</summary>
    [RelayCommand]
    private void NextSearchHit()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }
        int idx = SelectedSearchHit is null ? -1 : SearchResults.IndexOf(SelectedSearchHit);
        SelectedSearchHit = SearchResults[(idx + 1) % SearchResults.Count];
    }

    /// <summary>Перейти к предыдущему хиту; wrap в начале.</summary>
    [RelayCommand]
    private void PreviousSearchHit()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }
        int idx = SelectedSearchHit is null ? 0 : SearchResults.IndexOf(SelectedSearchHit);
        SelectedSearchHit = SearchResults[(idx - 1 + SearchResults.Count) % SearchResults.Count];
    }

    partial void OnSelectedSearchHitChanged(SearchHit? value)
    {
        if (value is not null && value.PageIndex != CurrentPageIndex)
        {
            // OnCurrentPageIndexChanged подхватит и refilter аннотаций, и render.
            CurrentPageIndex = value.PageIndex;
        }
    }

    public RenderOptions BuildRenderOptions() => new(Zoom, Theme: Theme, RenderAnnotations: true);

    public async Task RenderCurrentPageAsync(CancellationToken ct)
    {
        await RenderCurrentPageCoreAsync(ct);
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
    }

    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Search error must not crash the tab.")]
    private async Task RunSearchAsync()
    {
        SearchResults.Clear();
        SelectedSearchHit = null;

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        IsSearching = true;
        string trimmed = SearchText.Trim();
        try
        {
            var query = new SearchQuery(trimmed);
            IReadOnlyList<SearchHit> hits = await _searchService
                .SearchInDocumentAsync(_document, _filePath, query, CancellationToken.None);

            foreach (SearchHit hit in hits)
            {
                SearchResults.Add(hit);
            }
            PromoteRecentSearch(trimmed);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Search failed for '{Query}' in '{Title}'.", SearchText, Title);
        }
        finally
        {
            IsSearching = false;
        }
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

    public async ValueTask DisposeAsync()
    {
        CurrentRender?.Dispose();
        CurrentRender = null;
        await _document.DisposeAsync();
    }
}
