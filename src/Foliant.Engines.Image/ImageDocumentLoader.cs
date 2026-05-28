using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Image;

/// <summary>
/// Recognises raster images by extension (PNG/JPEG/BMP/TIFF/GIF) or by sniffing the first
/// ~12 bytes for a known magic signature. <see cref="LoadAsync"/> decodes the image in full
/// via SixLabors.ImageSharp on a worker thread and returns a one-page <see cref="IDocument"/>.
/// </summary>
public sealed class ImageDocumentLoader(ILogger<ImageDocumentLoader> log) : IDocumentLoader
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] BmpMagic = [0x42, 0x4D];
    private static readonly byte[] TiffLeMagic = [0x49, 0x49, 0x2A, 0x00];
    private static readonly byte[] TiffBeMagic = [0x4D, 0x4D, 0x00, 0x2A];
    private static readonly byte[] GifMagic = [0x47, 0x49, 0x46, 0x38];

    public DocumentKind Kind => DocumentKind.Image;

    public bool CanLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        if (".png".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".jpg".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".jpeg".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".bmp".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".tif".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".tiff".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".gif".Equals(ext, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasImageMagic(path);
    }

    public async Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Image file not found.", path);
        }

        return await Task.Run<IDocument>(() =>
        {
            ImageDocument doc = ImageDocument.Open(path);
            log.LogDebug("Loaded image '{Path}' via ImageSharp", path);
            return doc;
        }, ct).ConfigureAwait(false);
    }

    private static bool HasImageMagic(string path)
    {
        Span<byte> head = stackalloc byte[12];
        int read;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = fs.Read(head);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (read >= PngMagic.Length && head[..PngMagic.Length].SequenceEqual(PngMagic))
        {
            return true;
        }

        if (read >= JpegMagic.Length && head[..JpegMagic.Length].SequenceEqual(JpegMagic))
        {
            return true;
        }

        if (read >= BmpMagic.Length && head[..BmpMagic.Length].SequenceEqual(BmpMagic))
        {
            return true;
        }

        if (read >= TiffLeMagic.Length && head[..TiffLeMagic.Length].SequenceEqual(TiffLeMagic))
        {
            return true;
        }

        if (read >= TiffBeMagic.Length && head[..TiffBeMagic.Length].SequenceEqual(TiffBeMagic))
        {
            return true;
        }

        if (read >= GifMagic.Length && head[..GifMagic.Length].SequenceEqual(GifMagic))
        {
            return true;
        }

        return false;
    }
}
