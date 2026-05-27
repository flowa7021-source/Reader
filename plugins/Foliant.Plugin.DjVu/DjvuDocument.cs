using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Domain;

namespace Foliant.Plugin.DjVu;

/// <summary>
/// Out-of-process DjVu document. Each render shells out to <c>ddjvu -format=ppm</c>;
/// metadata and hidden text come from <c>djvused</c>. The document holds no native handle —
/// every operation is an isolated child-process call against the on-disk file.
/// </summary>
internal sealed class DjvuDocument : IDocument
{
    private readonly string _path;
    private readonly DjvuToolset _tools;
    private readonly ConcurrentDictionary<int, PageSize> _pageSizeCache = new();
    private bool _disposed;

    public DocumentKind Kind => DocumentKind.Djvu;
    public int PageCount { get; }
    public DocumentMetadata Metadata { get; }

    private DjvuDocument(string path, DjvuToolset tools, int pageCount, DocumentMetadata metadata)
    {
        _path = path;
        _tools = tools;
        PageCount = pageCount;
        Metadata = metadata;
    }

    public static async Task<DjvuDocument> OpenAsync(string path, DjvuToolset tools, CancellationToken ct)
    {
        string countOut = await DjvuProcessRunner
            .RunForTextAsync(tools.DjvusedPath, [path, "-e", "n"], ct)
            .ConfigureAwait(false);

        int pageCount = DjvusedOutput.ParsePageCount(countOut);
        return new DjvuDocument(path, tools, pageCount, DocumentMetadata.Empty);
    }

    public PageSize GetPageSize(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageIndex(pageIndex);

        // Synchronous facade over the async tool call (ConfigureAwait(false) ⇒ no context capture).
        return QueryPageSizeAsync(pageIndex, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageIndex(pageIndex);

        IReadOnlyList<string> args = await BuildRenderArgsAsync(pageIndex, opts, ct).ConfigureAwait(false);
        byte[] ppm = await DjvuProcessRunner
            .RunForBytesAsync(_tools.DdjvuPath, args, ct)
            .ConfigureAwait(false);

        PpmImage img = PpmParser.Parse(ppm);
        // Parser returns a freshly owned buffer, so in-place mutation is safe.
        RenderColorMap.ApplyTheme(MemoryMarshal.AsMemory(img.Bgra32).Span, opts.Theme);

        // ddjvu renders at native_size * zoom (see BuildRenderArgsAsync), so the zoom-independent
        // page size — the basis for annotation pixel↔page mapping — is recovered by dividing back.
        double zoom = opts.Zoom <= 0 ? 1.0 : opts.Zoom;
        var pageSize = new PageSize(img.Width / zoom, img.Height / zoom);
        return new DjvuPageRender(img.Width, img.Height, img.Stride, img.Bgra32, pageSize);
    }

    public async Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageIndex(pageIndex);

        try
        {
            string txt = await DjvuProcessRunner
                .RunForTextAsync(_tools.DjvusedPath, [_path, "-e", SelectScript(pageIndex, "print-txt")], ct)
                .ConfigureAwait(false);

            TextLayer layer = DjvusedOutput.ParseTextLayer(pageIndex, txt);
            return layer.Runs.Count == 0 ? null : layer;
        }
        catch (InvalidOperationException)
        {
            // No hidden-text chunk on this page — best-effort contract returns null.
            return null;
        }
    }

    public IDocumentEditor? GetEditor() => null;

    public IFormController? GetForms() => null;

    public ISignatureController? GetSignatures() => null;

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async Task<IReadOnlyList<string>> BuildRenderArgsAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        // ddjvu uses 1-based page numbers; pageIndex is 0-based.
        var args = new List<string>
        {
            "-format=ppm",
            string.Create(CultureInfo.InvariantCulture, $"-page={pageIndex + 1}"),
        };

        if (await TryBuildSizeArgAsync(pageIndex, opts, ct).ConfigureAwait(false) is { } sizeArg)
        {
            args.Add(sizeArg);
        }

        args.Add(_path);
        args.Add("-"); // stdout
        return args;
    }

    private async Task<string?> TryBuildSizeArgAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        if (Math.Abs(opts.Zoom - 1.0) < 0.001 && opts.MaxWidthPx is null && opts.MaxHeightPx is null)
        {
            return null; // native resolution
        }

        PageSize native = await QueryPageSizeAsync(pageIndex, ct).ConfigureAwait(false);
        if (native.WidthPt <= 0 || native.HeightPt <= 0)
        {
            return null;
        }

        int w = Clamp(native.WidthPt * opts.Zoom, opts.MaxWidthPx);
        int h = Clamp(native.HeightPt * opts.Zoom, opts.MaxHeightPx);
        return string.Create(CultureInfo.InvariantCulture, $"-size={w}x{h}");
    }

    // Размер страницы не меняется за время жизни документа — мемоизируем, чтобы не шеллить
    // djvused повторно (GetPageSize при ресайзе колонок + size-аргумент рендера зовут это же).
    private async Task<PageSize> QueryPageSizeAsync(int pageIndex, CancellationToken ct)
    {
        if (_pageSizeCache.TryGetValue(pageIndex, out PageSize cached))
        {
            return cached;
        }

        string sizeOut = await DjvuProcessRunner
            .RunForTextAsync(_tools.DjvusedPath, [_path, "-e", SelectScript(pageIndex, "size")], ct)
            .ConfigureAwait(false);

        PageSize size = DjvusedOutput.ParsePageSize(sizeOut);
        _pageSizeCache[pageIndex] = size;
        return size;
    }

    private void ValidatePageIndex(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex), pageIndex, $"Page index must be in [0, {PageCount}).");
        }
    }

    private static string SelectScript(int pageIndex, string command) =>
        string.Create(CultureInfo.InvariantCulture, $"select {pageIndex + 1}; {command}");

    private static int Clamp(double px, int? maxPx)
    {
        if (maxPx.HasValue && px > maxPx.Value)
        {
            px = maxPx.Value;
        }

        return Math.Max(1, (int)Math.Round(px));
    }
}
