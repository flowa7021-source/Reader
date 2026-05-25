using CommunityToolkit.Mvvm.ComponentModel;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IDocument _document;
    private readonly string _filePath;
    private readonly ISearchService _searchService;
    private readonly IAnnotationService _annotationService;
    private readonly IBookmarkService _bookmarkService;
    private readonly ISearchHistoryService? _searchHistory;
    private readonly IOcrPipelineService? _ocr;
    private readonly IFileFingerprint? _fingerprint;
    private readonly ILogger<DocumentTabViewModel> _logger;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    [NotifyPropertyChangedFor(nameof(IsCurrentPageBookmarked))]
    private int _currentPageIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private IPageRender? _currentRender;

    [ObservableProperty]
    private RenderTheme _theme = RenderTheme.Original;

    /// <summary>«N/M» — отображается в статус-баре. Локаль-агностичный формат: чисто цифры.</summary>
    public string PageInfo => $"{CurrentPageIndex + 1}/{Math.Max(PageCount, 1)}";

    /// <summary>Read-only обёртка над <see cref="IDocument.Metadata"/> для info-диалога.
    /// Создаётся лениво — пока пользователь не открыл «Document Info», VM не строится.</summary>
    public DocumentMetadataViewModel Metadata => _metadataLazy.Value;

    /// <summary>Путь к открытому файлу, как был передан в конструктор. Используется для
    /// dedupe-on-open в <see cref="MainViewModel"/> и для отладочных сообщений.</summary>
    public string FilePath => _filePath;

    private readonly Lazy<DocumentMetadataViewModel> _metadataLazy;

    public DocumentTabViewModel(
        IDocument document,
        string filePath,
        ISearchService searchService,
        IAnnotationService annotationService,
        IBookmarkService bookmarkService,
        ILogger<DocumentTabViewModel> logger,
        ISearchHistoryService? searchHistory = null,
        IOcrPipelineService? ocr = null,
        IFileFingerprint? fingerprint = null)
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
        _searchHistory = searchHistory;
        _ocr = ocr;
        _fingerprint = fingerprint;
        _logger = logger;
        Title = Path.GetFileName(filePath);
        PageCount = document.PageCount;
        _metadataLazy = new Lazy<DocumentMetadataViewModel>(
            () => new DocumentMetadataViewModel(_document.Metadata, _filePath, PageCount));

        if (_searchHistory is not null)
        {
            foreach (var q in _searchHistory.GetHistory())
            {
                RecentSearches.Add(q);
            }
        }

        // Computed counts биндятся в sidebar/status — пробрасываем
        // CollectionChanged → PropertyChanged для соответствующего count-property.
        CurrentPageAnnotations.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(CurrentPageAnnotationsCount));
        Bookmarks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BookmarksCount));
            OnPropertyChanged(nameof(IsCurrentPageBookmarked));
        };
        SearchResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SearchHitCount));
            OnPropertyChanged(nameof(SelectedSearchHitOneBasedIndex));
            OnPropertyChanged(nameof(SearchHitInfo));
        };
    }

    public async ValueTask DisposeAsync()
    {
        CurrentRender?.Dispose();
        CurrentRender = null;
        await _document.DisposeAsync();
    }
}
