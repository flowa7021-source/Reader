using CommunityToolkit.Mvvm.ComponentModel;
using Foliant.Application.Services;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel : ObservableObject, IAsyncDisposable
{
    private IDocument _document;
    private readonly string _filePath;
    private readonly ISearchService _searchService;
    private readonly IAnnotationService _annotationService;
    private readonly IBookmarkService _bookmarkService;
    private readonly ISearchHistoryService? _searchHistory;
    private readonly IOcrPipelineService? _ocr;
    private readonly IFileFingerprint? _fingerprint;
    private readonly IDocumentIndexer? _indexer;
    private readonly IPageEditService? _pageEdit;
    private readonly OpenDocumentUseCase? _openUseCase;
    private readonly ISettingsService? _settings;
    private readonly IAnnotatedPdfExportService? _annotatedPdfExporter;
    private readonly IBookmarkFormatCatalog? _bookmarkFormats;
    private readonly IPageRangeExtractor? _pageRangeExtractor;
    private readonly IPageImageExporter? _pageImageExporter;
    private readonly IPdfOutlineReader? _pdfOutlineReader;
    private readonly IWatermarkService? _watermarkService;
    private readonly IHeaderFooterService? _headerFooterService;
    private readonly ILogger<DocumentTabViewModel> _logger;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyPropertyChangedFor(nameof(CanDeleteCurrentPage))]
    [NotifyPropertyChangedFor(nameof(CanMovePageDown))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCurrentPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(MovePageDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunOcrCommand))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    [NotifyPropertyChangedFor(nameof(IsCurrentPageBookmarked))]
    [NotifyPropertyChangedFor(nameof(CanMovePageUp))]
    [NotifyPropertyChangedFor(nameof(CanMovePageDown))]
    [NotifyCanExecuteChangedFor(nameof(MovePageUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MovePageDownCommand))]
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

    /// <summary>Сайдбар «All Annotations» текущего документа: все аннотации, сгруппированные
    /// по странице, с фильтрами/экспортом. Перестраивается при каждой мутации списка.</summary>
    public AnnotationsDocumentViewModel AnnotationsDocument { get; }

    /// <summary>Ширина миниатюры в пикселях (фикс., не зависит от zoom).</summary>
    private const int ThumbnailWidthPx = 128;

    /// <summary>Лента миниатюр страниц: выбор страницы и drag-reorder. Выбор синхронизирован
    /// с <see cref="CurrentPageIndex"/>; reorder делегируется page-edit + reload.</summary>
    public ThumbnailStripViewModel Thumbnails { get; }

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
        IFileFingerprint? fingerprint = null,
        IDocumentIndexer? indexer = null,
        IPageEditService? pageEdit = null,
        OpenDocumentUseCase? openUseCase = null,
        ISettingsService? settings = null,
        IAnnotationExporter? annotationExporter = null,
        IAnnotatedPdfExportService? annotatedPdfExporter = null,
        IBookmarkFormatCatalog? bookmarkFormats = null,
        IPdfOutlineReader? pdfOutlineReader = null,
        IPageRangeExtractor? pageRangeExtractor = null,
        IPageImageExporter? pageImageExporter = null,
        IWatermarkService? watermarkService = null,
        IHeaderFooterService? headerFooterService = null)
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
        _indexer = indexer;
        _pageEdit = pageEdit;
        _openUseCase = openUseCase;
        _settings = settings;
        _annotatedPdfExporter = annotatedPdfExporter;
        _bookmarkFormats = bookmarkFormats;
        _pdfOutlineReader = pdfOutlineReader;
        _pageRangeExtractor = pageRangeExtractor;
        _pageImageExporter = pageImageExporter;
        _watermarkService = watermarkService;
        _headerFooterService = headerFooterService;
        _logger = logger;
        Title = Path.GetFileName(filePath);
        PageCount = document.PageCount;
        AnnotationsDocument = new AnnotationsDocumentViewModel(
            pageIndex => CurrentPageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, PageCount - 1)),
            annotationExporter);
        Thumbnails = new ThumbnailStripViewModel(
            PageCount, ReorderPagesViaStripAsync, SelectPageFromStrip,
            (index, token) => _document.RenderPageAsync(index, new RenderOptions(Zoom: 1.0, MaxWidthPx: ThumbnailWidthPx), token));
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
            OnPropertyChanged(nameof(CanExportBookmarks));
            ExportBookmarksCommand.NotifyCanExecuteChanged();
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
        _ocrCts?.Dispose();
        ClearVisiblePages();
        Thumbnails.Dispose();
        CurrentRender?.Dispose();
        CurrentRender = null;
        await _document.DisposeAsync();
    }
}
