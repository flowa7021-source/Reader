using Foliant.Application.Services;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Foliant.Engines.Epub;
using Foliant.Engines.Fb2;
using Foliant.Engines.Image;
using Foliant.Engines.Mobi;
using Foliant.Engines.Pdf;
using Foliant.Infrastructure.Annotations;
using Foliant.Infrastructure.Bookmarks;
using Foliant.Infrastructure.Caching;
using Foliant.Infrastructure.EventStore;
using Foliant.Infrastructure.Export;
using Foliant.Infrastructure.Search;
using Foliant.Infrastructure.Storage;
using Foliant.Rendering.Html;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foliant.E2E.Tests;

/// <summary>
/// A headless, Linux-runnable composition of the <b>real</b> document pipeline — the same cross-platform
/// services that <c>Foliant.App/AppHostBuilder</c> wires for document handling, minus the WPF layer
/// (views, print, password prompt, thumbnail/page-image rendering) and minus the Windows-runtime
/// concerns (DPAPI licensing, PaddleOCR models, update/crash/backup). It exists so end-to-end tests can
/// drive whole pipelines — open → render → text-layer → index/search → edit → export → annotate →
/// reopen — through the actual engines (PDFium, PdfPig, AngleSharp+SixLabors), real SQLite (FTS + disk
/// cache) and real on-disk sidecars, exactly as the shipping app does.
///
/// <para>All persistent state lives under a single temp directory that is deleted on disposal. Resolve
/// services via <see cref="Services"/> or the convenience accessors. Not thread-safe; one host per
/// test (or per fixture).</para>
/// </summary>
public sealed class FoliantPipelineHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    /// <summary>Creates a host with all state rooted at a fresh temp directory.</summary>
    /// <param name="configureExtra">Optional hook to register additional services (e.g. the DjVu
    /// plugin loader) on top of the in-box document pipeline.</param>
    public FoliantPipelineHost(Action<IServiceCollection>? configureExtra = null)
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "foliant-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);

        string cacheDir = SubDir("cache");
        string annotationsDir = SubDir("annotations");
        string bookmarksDir = SubDir("bookmarks");
        string autosaveDir = SubDir("autosave");
        string ftsPath = Path.Combine(SubDir("index"), "fts.db");

        var services = new ServiceCollection();
        services.AddLogging();

        // ── Infrastructure: fingerprint, caches, sidecars, event store, FTS ──
        services.AddSingleton<IFileFingerprint, FileFingerprint>();
        services.AddSingleton(_ => new MemoryPageCache(capacityBytes: 64L * 1024 * 1024));
        services.AddSingleton<IDiskCache>(sp =>
            new SqliteDiskCache(cacheDir, sp.GetRequiredService<ILogger<SqliteDiskCache>>()));

        services.AddSingleton<IAnnotationStore>(sp =>
            new JsonAnnotationStore(annotationsDir, sp.GetRequiredService<ILogger<JsonAnnotationStore>>()));
        services.AddSingleton<IAnnotationService, AnnotationService>();

        services.AddSingleton<IBookmarkStore>(sp =>
            new JsonBookmarkStore(bookmarksDir, sp.GetRequiredService<ILogger<JsonBookmarkStore>>()));
        services.AddSingleton<IBookmarkService, BookmarkService>();

        services.AddSingleton<IEventStore>(sp =>
            new JsonlEventStore(autosaveDir, sp.GetRequiredService<ILogger<JsonlEventStore>>()));

        services.AddSingleton<IFtsIndex>(sp =>
            new SqliteFtsIndex(ftsPath, sp.GetRequiredService<ILogger<SqliteFtsIndex>>()));
        services.AddSingleton<DocumentIndexingService>();
        services.AddSingleton<IDocumentIndexer>(sp => sp.GetRequiredService<DocumentIndexingService>());

        // ── Rendering: shared HTML engine (EPUB/FB2/MOBI) ──
        services.AddSingleton<FontStore>();
        services.AddSingleton<IHtmlRenderer, HtmlRenderer>();

        // ── Document loaders (the real OpenDocumentUseCase routing surface) ──
        services.AddSingleton<IDocumentLoader, PdfDocumentLoader>();
        services.AddSingleton<IDocumentLoader, ImageDocumentLoader>();
        services.AddSingleton<IDocumentLoader, EpubDocumentLoader>();
        services.AddSingleton<IDocumentLoader, Fb2DocumentLoader>();
        services.AddSingleton<IDocumentLoader, MobiDocumentLoader>();

        // ── Application use-cases ──
        services.AddSingleton<OpenDocumentUseCase>();
        services.AddSingleton<ISearchService, SearchService>();

        // ── Export (plain text + DOCX) ──
        services.AddSingleton<IDocumentExportService, PlainTextDocumentExportService>();
        services.AddSingleton<IDocumentExportService, DocxDocumentExportService>();

        // ── PDF structural editing (round-trip surface) ──
        services.AddSingleton<IPageEditService, PdfPageEditService>();
        services.AddSingleton<IPdfMetadataEditService, PdfPigMetadataEditService>();
        services.AddSingleton<IWatermarkService, PdfiumWatermarkService>();
        services.AddSingleton<IAnnotatedPdfExportService, AnnotatedPdfExportService>();

        configureExtra?.Invoke(services);

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The composed service provider.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Root temp directory; deleted on disposal.</summary>
    public string TempRoot { get; }

    /// <summary>Resolves a required service.</summary>
    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Opens a document through the real loader-routing use-case (the production open path).</summary>
    public Task<IDocument> OpenAsync(string path, CancellationToken ct = default) =>
        Get<OpenDocumentUseCase>().ExecuteAsync(path, ct);

    /// <summary>A scratch path under the host's temp root (for outputs written by a test).</summary>
    public string ScratchPath(string fileName) => Path.Combine(TempRoot, fileName);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);

        try
        {
            Directory.Delete(TempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string SubDir(string name)
    {
        string dir = Path.Combine(TempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
