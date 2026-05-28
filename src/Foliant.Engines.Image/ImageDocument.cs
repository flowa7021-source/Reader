using Foliant.Domain;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.Engines.Image;

/// <summary>
/// One-page document backed by a decoded image. The pixel buffer is decoded ONCE on open
/// (ImageSharp <see cref="Bgra32"/> matches the <see cref="IPageRender.Bgra32"/> byte layout),
/// so rendering is a cheap zero-copy handoff. Page size is reported in pixels — the convention
/// for images, since they have no intrinsic page-unit (point) coordinate system.
/// </summary>
internal sealed class ImageDocument : IDocument
{
    private readonly string _path;
    private readonly int _widthPx;
    private readonly int _heightPx;
    private readonly int _stride;
    private readonly byte[] _bgra32;

    public DocumentKind Kind => DocumentKind.Image;
    public int PageCount => 1;
    public DocumentMetadata Metadata => DocumentMetadata.Empty;

    private ImageDocument(string path, int widthPx, int heightPx, int stride, byte[] bgra32)
    {
        _path = path;
        _widthPx = widthPx;
        _heightPx = heightPx;
        _stride = stride;
        _bgra32 = bgra32;
    }

    public static ImageDocument Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Image file not found.", path);
        }

        using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
        int w = image.Width;
        int h = image.Height;
        int stride = w * 4;
        var bgra = new byte[h * stride];
        image.CopyPixelDataTo(bgra);
        return new ImageDocument(path, w, h, stride, bgra);
    }

    public PageSize GetPageSize(int pageIndex)
    {
        ValidatePageIndex(pageIndex);
        return new PageSize(_widthPx, _heightPx);
    }

    public Task<IPageRender> RenderPageAsync(int pageIndex, RenderOptions opts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ValidatePageIndex(pageIndex);
        ct.ThrowIfCancellationRequested();

        // Zoom is intentionally ignored — we return native pixels and let the UI scale.
        // Theme is ignored too: the DjVu RenderColorMap lives in the DjVu plugin and we won't
        // take a dependency on it. Callers tolerate Theme.Original for the image engine.
        var pageSize = new PageSize(_widthPx, _heightPx);
        IPageRender render = new ImagePageRender(_widthPx, _heightPx, _stride, _bgra32, pageSize);
        return Task.FromResult(render);
    }

    public Task<TextLayer?> GetTextLayerAsync(int pageIndex, CancellationToken ct)
    {
        ValidatePageIndex(pageIndex);
        return Task.FromResult<TextLayer?>(null);
    }

    public IDocumentEditor? GetEditor() => null;

    public IFormController? GetForms() => null;

    public ISignatureController? GetSignatures() => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void ValidatePageIndex(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex), pageIndex, $"Page index must be in [0, {PageCount}).");
        }
    }
}
