using System.Globalization;
using System.IO;
using System.Net.Http;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Foliant.Engines.Epub;
using Foliant.Engines.Fb2;
using Foliant.Engines.Image;
using Foliant.Engines.Mobi;
using Foliant.Engines.Ocr;
using Foliant.Engines.Pdf;
using Foliant.Infrastructure.Annotations;
using Foliant.Infrastructure.Bookmarks;
using Foliant.Infrastructure.Caching;
using Foliant.Infrastructure.Diagnostics;
using Foliant.Infrastructure.EventStore;
using Foliant.Infrastructure.Export;
using Foliant.Infrastructure.Licensing;
using Foliant.Infrastructure.Plugins;
using Foliant.Infrastructure.Search;
using Foliant.Infrastructure.Settings;
using Foliant.Infrastructure.Storage;
using Foliant.Infrastructure.Updates;
using Foliant.UI;
using Foliant.UI.Imaging;
using Foliant.UI.Localization;
using Foliant.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Foliant.App.Composition;

// Названо AppHostBuilder, чтобы не сталкиваться с Microsoft.Extensions.Hosting.HostBuilder
// (тот публичен и попадает в скоуп через `using Microsoft.Extensions.Hosting`).
internal static class AppHostBuilder
{
    public static IHost Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var builder = Host.CreateApplicationBuilder(args);

        ConfigureLogging(builder);
        ConfigureServices(builder.Services);

        return builder.Build();
    }

    private static void ConfigureLogging(HostApplicationBuilder builder)
    {
        var logFile = Path.Combine(AppPaths.Logs, "foliant-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                path: logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IFileFingerprint, FileFingerprint>();
        services.AddSingleton<ISettingsStore>(sp =>
            new JsonSettingsStore(AppPaths.SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IRecentsService, RecentsService>();
        services.AddSingleton<ILocalizationService>(LocalizationManager.Instance);

        // Cache (RAM + Disk). Жёсткий потолок RAM: min(15 % системной, 1 ГБ); по плану.
        var ramLimit = Math.Min(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 100 * 15, 1L * 1024 * 1024 * 1024);
        var memoryCacheCapacity = Math.Max(ramLimit, 128L * 1024 * 1024);
        // Factory-form регистрация — DI владеет lifecycle и сам диспозит на shutdown.
        // Прямой AddSingleton(new MemoryPageCache(...)) НЕ диспозится, что ловит CA2000.
        services.AddSingleton(_ => new MemoryPageCache(capacityBytes: memoryCacheCapacity));
        services.AddSingleton<IDiskCache>(sp =>
            new SqliteDiskCache(AppPaths.Cache, sp.GetRequiredService<ILogger<SqliteDiskCache>>()));
        services.AddSingleton<IOcrCache, OcrDiskCache>();
        services.AddSingleton<ThumbnailRenderer>();

        // OCR pipeline (PaddleOCR in-process). Движок — синглтон (держит загруженные модели,
        // сериализует вызовы). ITextLayerCache не зарегистрирован → pipeline получает null (ok).
        services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
        services.AddSingleton<OcrPageUseCase>();
        services.AddSingleton<IOcrPipelineService, OcrPipelineService>();

        // Annotations — JSON sidecar (Phase 1). Phase 2: embed in PDF via PdfPig.
        services.AddSingleton<IAnnotationStore>(sp =>
            new JsonAnnotationStore(AppPaths.Annotations, sp.GetRequiredService<ILogger<JsonAnnotationStore>>()));
        services.AddSingleton<IAnnotationService, AnnotationService>();

        // Bookmarks — JSON sidecar (parallel to annotations).
        services.AddSingleton<IBookmarkStore>(sp =>
            new JsonBookmarkStore(AppPaths.Bookmarks, sp.GetRequiredService<ILogger<JsonBookmarkStore>>()));
        services.AddSingleton<IBookmarkService, BookmarkService>();

        // Search history — персистируется между сессиями (Program.cs вызывает LoadAsync на старте,
        // мутации сохраняются fire-and-forget). Singleton — общая история для всех вкладок.
        services.AddSingleton<ISearchHistoryService>(sp =>
            new JsonSearchHistoryService(
                AppPaths.SearchHistoryFile,
                sp.GetRequiredService<ILogger<JsonSearchHistoryService>>()));

        // Cache janitor — фоновая эвикция.
        services.AddSingleton(new CacheJanitorOptions());
        services.AddHostedService<CacheJanitor>();

        // Search index (FTS5).
        services.AddSingleton<IFtsIndex>(sp =>
            new SqliteFtsIndex(
                Path.Combine(AppPaths.Cache, "index", "fts.db"),
                sp.GetRequiredService<ILogger<SqliteFtsIndex>>()));

        // Background document indexer — fires on every document open, populates FTS5.
        services.AddSingleton<DocumentIndexingService>();
        services.AddSingleton<IDocumentIndexer>(sp => sp.GetRequiredService<DocumentIndexingService>());
        services.AddHostedService(sp => sp.GetRequiredService<DocumentIndexingService>());

        // Event store (append-only JSONL в Autosave/{fingerprint}) — undo/redo + crash recovery.
        // PdfDocumentLoader подхватит его опционально → IDocument.GetEditor() станет доступен.
        services.AddSingleton<IEventStore>(sp =>
            new JsonlEventStore(AppPaths.Autosave, sp.GetRequiredService<ILogger<JsonlEventStore>>()));

        // Document engines (loaders регистрируются как IDocumentLoader; OpenDocumentUseCase
        // получает IEnumerable<IDocumentLoader> и выбирает по факту CanLoad).
        services.AddSingleton<IDocumentLoader, PdfDocumentLoader>();
        // In-box image loader (PNG/JPEG/BMP/TIFF/GIF) — ImageSharp, pure managed.
        services.AddSingleton<IDocumentLoader, ImageDocumentLoader>();
        services.AddSingleton<IDocumentLoader, EpubDocumentLoader>();
        services.AddSingleton<IDocumentLoader, Fb2DocumentLoader>();
        services.AddSingleton<IDocumentLoader, MobiDocumentLoader>();

        // Engine-плагины (MEF2) из <appdir>/plugins: каждый IEnginePlugin.Loader становится
        // IDocumentLoader. Так подключается DjVu (и будущие форматы) без compile-time ссылки.
        // Discovery — eager (на старте композиции); NullLogger т.к. DI-провайдера ещё нет.
        var pluginCatalog = new PluginCatalog(NullLogger<PluginCatalog>.Instance);
        foreach (var plugin in pluginCatalog.Discover(AppPaths.Plugins))
        {
            services.AddSingleton<IDocumentLoader>(plugin.Loader);
        }

        services.AddSingleton<IPageEditService, PdfPageEditService>();
        services.AddSingleton<IPageRangeExtractor, PdfPigPageRangeExtractor>();
        services.AddSingleton<IWatermarkService, PdfiumWatermarkService>();
        services.AddSingleton<IHeaderFooterService, PdfiumHeaderFooterService>();
        services.AddSingleton<IPdfCropService, PdfiumCropService>();
        services.AddSingleton<IPdfFormFillService, PdfiumFormFillService>();
        services.AddSingleton<IPdfMergeService, PdfiumMergeService>();
        services.AddSingleton<IRedactionService, PdfiumRedactionService>();
        services.AddSingleton<IFindAndRedactService, FindAndRedactService>();
        services.AddSingleton<IPdfSplitService, PdfPigSplitService>();
        services.AddSingleton<IBatesNumberingService, PdfiumBatesNumberingService>();
        services.AddSingleton<IPdfMetadataEditService, PdfPigMetadataEditService>();
        services.AddSingleton<IPdfOcgService, PdfiumOcgService>();
        services.AddSingleton<IPdfPageLabelService, PdfPigPageLabelService>();
        services.AddSingleton<IPdfViewerPreferencesService, PdfPigViewerPreferencesService>();
        services.AddSingleton<IPdfAttachmentService, PdfPigAttachmentService>();
        services.AddSingleton<IPdfInsertPagesService, PdfPigInsertPagesService>();
        services.AddSingleton<IPdfXmpService, PdfPigXmpService>();
        services.AddSingleton<IPdfSanitizationService, PdfPigSanitizationService>();
        services.AddSingleton<IBatchJobRunner>(sp => new BatchJobRunner(
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<BatchJobRunner>(),
            watermark: sp.GetService<IWatermarkService>(),
            headerFooter: sp.GetService<IHeaderFooterService>(),
            crop: sp.GetService<IPdfCropService>()));
        services.AddSingleton<IPageImageExporter, WpfPageImageExporter>();
        services.AddSingleton<IPdfOutlineReader, PdfPigOutlineReader>();
        services.AddSingleton<IPdfOutlineWriter, PdfPigOutlineWriter>();

        // Application
        services.AddSingleton<OpenDocumentUseCase>();
        services.AddSingleton<ISearchService, SearchService>();

        // Document export — потребитель выбирает реализацию по CanExport(targetFormat).
        services.AddSingleton<IDocumentExportService, PlainTextDocumentExportService>();
        services.AddSingleton<IDocumentExportService, DocxDocumentExportService>();

        // Annotation interchange (Q-F17). All exporters/importers register here; the catalog
        // resolves the right one by file extension for the save/open dialogs. JSON is also
        // registered as a concrete type so the sidebar's single-exporter path stays JSON
        // regardless of registration order (MS DI hands a bare IAnnotationExporter the LAST
        // registration, not the first).
        services.AddSingleton<JsonAnnotationExporter>();
        services.AddSingleton<IAnnotationExporter>(sp => sp.GetRequiredService<JsonAnnotationExporter>());
        services.AddSingleton<IAnnotationExporter, MarkdownAnnotationExporter>();
        services.AddSingleton<IAnnotationExporter, XfdfAnnotationExporter>();
        services.AddSingleton<IAnnotationExporter, FdfAnnotationExporter>();
        services.AddSingleton<IAnnotationImporter, XfdfAnnotationImporter>();
        services.AddSingleton<IAnnotationImporter, JsonAnnotationImporter>();
        services.AddSingleton<IAnnotationImporter, FdfAnnotationImporter>();
        services.AddSingleton<IAnnotationFormatCatalog, AnnotationFormatCatalog>();

        // Bookmark exchange formats — каталог собирает по DI (см. IBookmarkFormatCatalog).
        // Json — также первый IBookmarkExporter в порядке регистрации, что важно для UI default'а.
        services.AddSingleton<JsonBookmarkExporter>();
        services.AddSingleton<IBookmarkExporter>(sp => sp.GetRequiredService<JsonBookmarkExporter>());
        services.AddSingleton<IBookmarkExporter, MarkdownBookmarkExporter>();
        services.AddSingleton<IBookmarkExporter, XfdfBookmarkExporter>();
        services.AddSingleton<IBookmarkImporter, JsonBookmarkImporter>();
        services.AddSingleton<IBookmarkImporter, XfdfBookmarkImporter>();
        services.AddSingleton<IBookmarkFormatCatalog, BookmarkFormatCatalog>();

        // Form-data exchange formats (Q-F24). JSON / FDF / XFDF. PDFium-side reader (фактическое
        // чтение значений из открытого PDF) — следующий PR; здесь только plumbing форматов.
        services.AddSingleton<IFormDataExporter, JsonFormDataExporter>();
        services.AddSingleton<IFormDataExporter, FdfFormDataExporter>();
        services.AddSingleton<IFormDataExporter, XfdfFormDataExporter>();
        services.AddSingleton<IFormDataImporter, JsonFormDataImporter>();
        services.AddSingleton<IFormDataImporter, FdfFormDataImporter>();
        services.AddSingleton<IFormDataImporter, XfdfFormDataImporter>();
        services.AddSingleton<IFormDataFormatCatalog, FormDataFormatCatalog>();
        services.AddSingleton<IPdfFormReader, PdfPigFormReader>();

        // Export a PDF with sidecar annotations embedded as real editable objects (Q-F17 Phase 2).
        services.AddSingleton<IAnnotatedPdfExportService, AnnotatedPdfExportService>();

        // Licensing storage + trial (Windows-only persistence: DPAPI + HKCU + marker).
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILicenseStorage, DpapiLicenseStorage>();
        services.AddSingleton<TrialStores>();
        services.AddSingleton<ITrialService, TrialPersistenceService>();

        // License verification — ECDSA P-256 over the embedded public key (LicenseKeys.PublicKeyPem).
        services.AddSingleton<ILicenseVerifier>(_ =>
            new EcdsaLicenseVerifier(LicenseKeys.PublicKeyPem, LicenseKeys.RevokedSignatureHashes));
        services.AddSingleton<ILicenseManager, LicenseManager>();

        // Crash reporting (§3.1: opt-in). Writes JSON reports to %LOCALAPPDATA%\Foliant\CrashReports.
        services.AddSingleton<ICrashReporter>(sp =>
            new FileCrashReporter(
                sp.GetRequiredService<ISettingsService>(),
                AppPaths.CrashReports,
                sp.GetRequiredService<TimeProvider>()));

        // Backup user data before an upgrade (§7.4). Only non-secret, upgrade-migratable data:
        // settings (schema-versioned) + autosave. license.key/trial.dat are DPAPI- and
        // machine-bound and are not touched by an in-place upgrade, so duplicating them into a
        // backup adds no recovery value and would copy a credential — deliberately excluded.
        services.AddSingleton<IBackupService>(sp =>
            new FileBackupService(
                AppPaths.Backup,
                [AppPaths.SettingsFile],
                [AppPaths.Autosave],
                sp.GetRequiredService<ILogger<FileBackupService>>()));

        // Update check (PROJECT_BOARD §7.4): GitHub Releases, once/day, opt-out via settings.
        services.AddHttpClient(GitHubReleaseSource.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Foliant-UpdateCheck");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IReleaseSource>(sp => new GitHubReleaseSource(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubReleaseSource.HttpClientName),
            sp.GetRequiredService<ILogger<GitHubReleaseSource>>()));
        services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();

        // ViewModels
        services.AddTransient<Func<IDocument, string, DocumentTabViewModel>>(sp =>
            (doc, path) => new DocumentTabViewModel(
                doc,
                path,
                sp.GetRequiredService<ISearchService>(),
                sp.GetRequiredService<IAnnotationService>(),
                sp.GetRequiredService<IBookmarkService>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<DocumentTabViewModel>(),
                searchHistory: sp.GetService<ISearchHistoryService>(),
                ocr: sp.GetService<IOcrPipelineService>(),
                fingerprint: sp.GetService<IFileFingerprint>(),
                indexer: sp.GetService<IDocumentIndexer>(),
                pageEdit: sp.GetService<IPageEditService>(),
                openUseCase: sp.GetService<OpenDocumentUseCase>(),
                settings: sp.GetService<ISettingsService>(),
                annotationExporter: sp.GetService<JsonAnnotationExporter>(),
                annotatedPdfExporter: sp.GetService<IAnnotatedPdfExportService>(),
                bookmarkFormats: sp.GetService<IBookmarkFormatCatalog>(),
                pdfOutlineReader: sp.GetService<IPdfOutlineReader>(),
                outlineWriter: sp.GetService<IPdfOutlineWriter>(),
                pageRangeExtractor: sp.GetService<IPageRangeExtractor>(),
                pageImageExporter: sp.GetService<IPageImageExporter>(),
                watermarkService: sp.GetService<IWatermarkService>(),
                headerFooterService: sp.GetService<IHeaderFooterService>(),
                cropService: sp.GetService<IPdfCropService>(),
                pdfFormReader: sp.GetService<IPdfFormReader>(),
                pdfFormFillService: sp.GetService<IPdfFormFillService>(),
                formDataFormatCatalog: sp.GetService<IFormDataFormatCatalog>(),
                redactionService: sp.GetService<IRedactionService>(),
                findAndRedactService: sp.GetService<IFindAndRedactService>(),
                splitService: sp.GetService<IPdfSplitService>(),
                batesService: sp.GetService<IBatesNumberingService>(),
                metadataEditService: sp.GetService<IPdfMetadataEditService>(),
                ocgService: sp.GetService<IPdfOcgService>(),
                printService: sp.GetService<IPrintService>(),
                pageLabelService: sp.GetService<IPdfPageLabelService>(),
                viewerPreferencesService: sp.GetService<IPdfViewerPreferencesService>(),
                attachmentService: sp.GetService<IPdfAttachmentService>(),
                insertPagesService: sp.GetService<IPdfInsertPagesService>(),
                xmpService: sp.GetService<IPdfXmpService>(),
                sanitizationService: sp.GetService<IPdfSanitizationService>()));

        // MainViewModel still resolves ILicenseManager/ITrialService via GetService so the
        // unit-test composition (which omits them) keeps working.
        services.AddTransient(sp => new MainViewModel(
            sp.GetRequiredService<OpenDocumentUseCase>(),
            sp.GetRequiredService<Func<IDocument, string, DocumentTabViewModel>>(),
            sp.GetRequiredService<IRecentsService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<IDocumentIndexer>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<MainViewModel>(),
            sp.GetService<ILicenseManager>(),
            sp.GetService<ITrialService>(),
            sp.GetService<IUpdateCheckService>(),
            sp.GetService<IPdfMergeService>(),
            sp.GetService<IPasswordPrompt>()));
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LicenseImportViewModel>();

        // Password prompt for opening encrypted PDFs (read-side decrypt). MainViewModel resolves
        // it via GetService so the unit-test composition (which omits it) keeps working.
        services.AddSingleton<IPasswordPrompt, WpfPasswordPrompt>();

        // Print service (File → Print, Ctrl+P). UI-impl сам показывает PrintDialog и рендерит
        // выбранный диапазон через IDocument.RenderPageAsync → document-neutral.
        // DocumentTabViewModel резолвит сервис через GetService — в headless/тестах не нужен.
        services.AddSingleton<IPrintService, WpfPrintService>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
        services.AddTransient<CrashRecoveryViewModel>();
        services.AddTransient<CrashRecoveryWindow>();
        services.AddTransient<Func<CrashRecoveryWindow>>(sp => () => sp.GetRequiredService<CrashRecoveryWindow>());
        services.AddTransient<LicenseImportWindow>();
        services.AddTransient<Func<LicenseImportWindow>>(sp => () => sp.GetRequiredService<LicenseImportWindow>());
        services.AddTransient(sp => new BatchViewModel(
            sp.GetRequiredService<IBatchJobRunner>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<BatchViewModel>()));
        services.AddTransient<BatchWindow>();
        services.AddTransient<Func<BatchWindow>>(sp => () => sp.GetRequiredService<BatchWindow>());
    }
}
