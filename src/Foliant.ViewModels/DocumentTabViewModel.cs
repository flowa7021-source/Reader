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
    private readonly ILogger<DocumentTabViewModel> _logger;
    private readonly List<Annotation> _allAnnotations = [];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private int _pageCount;

    [ObservableProperty]
    private int _currentPageIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private IPageRender? _currentRender;

    [ObservableProperty]
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

    public DocumentTabViewModel(
        IDocument document,
        string filePath,
        ISearchService searchService,
        IAnnotationService annotationService,
        ILogger<DocumentTabViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(annotationService);
        ArgumentNullException.ThrowIfNull(logger);

        _document = document;
        _filePath = filePath;
        _searchService = searchService;
        _annotationService = annotationService;
        _logger = logger;
        Title = Path.GetFileName(filePath);
        PageCount = document.PageCount;
    }

    partial void OnCurrentPageIndexChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, Math.Max(0, PageCount - 1));
        if (clamped != value)
        {
            CurrentPageIndex = clamped;
            return;
        }
        RefreshCurrentPageAnnotations();
        _ = RenderCurrentPageAsync(CancellationToken.None);
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
        try
        {
            var query = new SearchQuery(SearchText.Trim());
            IReadOnlyList<SearchHit> hits = await _searchService
                .SearchInDocumentAsync(_document, _filePath, query, CancellationToken.None);

            foreach (SearchHit hit in hits)
            {
                SearchResults.Add(hit);
            }
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
