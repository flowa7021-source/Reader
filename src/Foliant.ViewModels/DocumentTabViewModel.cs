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
    private readonly IDocument _document;
    private readonly string _filePath;
    private readonly ISearchService _searchService;
    private readonly ILogger<DocumentTabViewModel> _logger;

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

    public DocumentTabViewModel(
        IDocument document,
        string filePath,
        ISearchService searchService,
        ILogger<DocumentTabViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(logger);

        _document = document;
        _filePath = filePath;
        _searchService = searchService;
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
        }
    }

    partial void OnSelectedSearchHitChanged(SearchHit? value)
    {
        if (value is not null && value.PageIndex != CurrentPageIndex)
        {
            CurrentPageIndex = value.PageIndex;
            _ = RenderCurrentPageAsync(CancellationToken.None);
        }
    }

    public RenderOptions BuildRenderOptions() => new(Zoom, Theme: Theme, RenderAnnotations: true);

    public async Task RenderCurrentPageAsync(CancellationToken ct)
    {
        await RenderCurrentPageCoreAsync(ct).ConfigureAwait(false);
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
                .SearchInDocumentAsync(_document, _filePath, query, CancellationToken.None)
                .ConfigureAwait(false);

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
            IPageRender result = await _document.RenderPageAsync(CurrentPageIndex, BuildRenderOptions(), ct).ConfigureAwait(false);
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
        await _document.DisposeAsync().ConfigureAwait(false);
    }
}
