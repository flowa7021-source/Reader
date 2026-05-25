using System.Buffers.Binary;
using Foliant.Domain;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Рендерит маленькие миниатюры страниц через <see cref="IDocument.RenderPageAsync"/>
/// и кэширует их в per-document <see cref="ThumbnailCache"/> по схеме
/// get-or-render-then-put.
///
/// <para><see cref="ThumbnailCache"/> хранит только <c>byte[]</c> без геометрии,
/// поэтому байты складываются как 12-байтный little-endian заголовок
/// (Width, Height, Stride) + сырой BGRA32. Кодировщик (PNG/JPEG) сознательно
/// не используется: WPF-слой разворачивает пиксели в <c>BitmapSource</c> сам.</para>
///
/// Concurrency: безопасность гарантирует сам <see cref="ThumbnailCache"/>
/// (lock на словаре); двойной рендер при гонке безвреден — побеждает
/// последний <c>Put</c>, оба дают идентичный результат.
/// </summary>
public sealed class ThumbnailRenderer(int maxWidthPx = DefaultMaxWidthPx)
{
    public const int DefaultMaxWidthPx = 128;

    private const int HeaderBytes = sizeof(int) * 3;

    private readonly RenderOptions _options = new(
        Zoom: 1.0,
        MaxWidthPx: PositiveOrThrow(maxWidthPx),
        RenderAnnotations: false);

    /// <summary>
    /// Вернуть миниатюру страницы из кэша или, если её там нет, отрендерить
    /// маленькую версию и положить в кэш. Cancellation пробрасывается из
    /// <see cref="IDocument.RenderPageAsync"/>.
    /// </summary>
    public async Task<Thumbnail> GetThumbnailAsync(
        IDocument document,
        ThumbnailCache cache,
        int pageIndex,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        if (cache.TryGet(pageIndex, out var cached))
        {
            return Decode(cached);
        }

        ct.ThrowIfCancellationRequested();

        using var render = await document
            .RenderPageAsync(pageIndex, _options, ct)
            .ConfigureAwait(false);

        var thumb = new Thumbnail(render.WidthPx, render.HeightPx, render.Stride, render.Bgra32.ToArray());
        cache.Put(pageIndex, Encode(thumb));
        return thumb;
    }

    private static byte[] Encode(Thumbnail thumb)
    {
        var pixels = thumb.Bgra32.Span;
        var buffer = new byte[HeaderBytes + pixels.Length];
        var header = buffer.AsSpan(0, HeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header, thumb.WidthPx);
        BinaryPrimitives.WriteInt32LittleEndian(header[sizeof(int)..], thumb.HeightPx);
        BinaryPrimitives.WriteInt32LittleEndian(header[(sizeof(int) * 2)..], thumb.Stride);
        pixels.CopyTo(buffer.AsSpan(HeaderBytes));
        return buffer;
    }

    private static Thumbnail Decode(byte[] stored)
    {
        // FRAGILE: формат должен совпадать с Encode (12-байтный LE заголовок).
        // Кэш per-document и in-process, так что версионирование не нужно.
        var header = stored.AsSpan(0, HeaderBytes);
        var width = BinaryPrimitives.ReadInt32LittleEndian(header);
        var height = BinaryPrimitives.ReadInt32LittleEndian(header[sizeof(int)..]);
        var stride = BinaryPrimitives.ReadInt32LittleEndian(header[(sizeof(int) * 2)..]);
        var pixels = stored.AsMemory(HeaderBytes);
        return new Thumbnail(width, height, stride, pixels);
    }

    private static int PositiveOrThrow(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value;
    }
}
