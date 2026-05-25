using System.Globalization;
using System.IO;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Foliant.Engines.Ocr;
using Foliant.Engines.Pdf;
using Foliant.Infrastructure.Annotations;
using Foliant.Infrastructure.Bookmarks;
using Foliant.Infrastructure.Caching;
using Foliant.Infrastructure.EventStore;
using Foliant.Infrastructure.Export;
using Foliant.Infrastructure.Licensing;
using Foliant.Infrastructure.Search;
using Foliant.Infrastructure.Settings;
using Foliant.Infrastructure.Storage;
using Foliant.UI;
using Foliant.UI.Localization;
using Foliant.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        // Application
        services.AddSingleton<OpenDocumentUseCase>();
        services.AddSingleton<ISearchService, SearchService>();

        // Document export — потребитель выбирает реализацию по CanExport(targetFormat).
        services.AddSingleton<IDocumentExportService, PlainTextDocumentExportService>();
        services.AddSingleton<IDocumentExportService, DocxDocumentExportService>();

        // Licensing storage + trial (Windows-only persistence: DPAPI + HKCU + marker).
        // ILicenseManager (требует публичный ключ верификатора) подключается на UI-этапе.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILicenseStorage, DpapiLicenseStorage>();
        services.AddSingleton<TrialStores>();
        services.AddSingleton<ITrialService, TrialPersistenceService>();

        // ViewModels
        services.AddTransient<Func<IDocument, string, DocumentTabViewModel>>(sp =>
            (doc, path) => new DocumentTabViewModel(
                doc,
                path,
                sp.GetRequiredService<ISearchService>(),
                sp.GetRequiredService<IAnnotationService>(),
                sp.GetRequiredService<IBookmarkService>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<DocumentTabViewModel>(),
                ocr: sp.GetService<IOcrPipelineService>(),
                fingerprint: sp.GetService<IFileFingerprint>()));

        // ILicenseManager — optional: dev-сборка может не регистрировать его
        // (нет публичного ключа в скоупе тестов). Factory-DI отдаёт null когда сервис
        // не зарегистрирован — MainViewModel это допустимо.
        services.AddTransient(sp => new MainViewModel(
            sp.GetRequiredService<OpenDocumentUseCase>(),
            sp.GetRequiredService<Func<IDocument, string, DocumentTabViewModel>>(),
            sp.GetRequiredService<IRecentsService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<IDocumentIndexer>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<MainViewModel>(),
            sp.GetService<ILicenseManager>()));
        services.AddTransient<SettingsViewModel>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
        services.AddTransient<CrashRecoveryViewModel>();
        services.AddTransient<CrashRecoveryWindow>();
        services.AddTransient<Func<CrashRecoveryWindow>>(sp => () => sp.GetRequiredService<CrashRecoveryWindow>());
    }
}
