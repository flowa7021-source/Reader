using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Адаптер: <see cref="IOcrCache"/> поверх <see cref="IDiskCache"/>.
/// Сериализация — System.Text.Json (source-gen) + GZip (SmallestSize), что для
/// текстовых слоёв даёт типичный коэффициент 5–10× и держит OCR-страницы на диске
/// в десятках КБ.
/// </summary>
public sealed class OcrDiskCache(IDiskCache disk, ILogger<OcrDiskCache> log) : IOcrCache
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Corrupt cache entry must not crash OCR; we log and treat as miss.")]
    public async Task<TextLayer?> TryGetAsync(CacheKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bytes = await disk.TryGetAsync(key, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            var dto = await JsonSerializer
                .DeserializeAsync(gz, OcrCacheJsonContext.Default.TextLayerDto, ct)
                .ConfigureAwait(false);
            return dto?.ToTextLayer();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            log.LogWarning(ex, "Corrupt OCR cache entry for {Key}; treating as miss", key);
            return null;
        }
    }

    public async Task PutAsync(CacheKey key, TextLayer layer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(layer);

        using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await JsonSerializer
                .SerializeAsync(gz, TextLayerDto.From(layer), OcrCacheJsonContext.Default.TextLayerDto, ct)
                .ConfigureAwait(false);
        }
        await disk.PutAsync(key, ms.ToArray(), ct).ConfigureAwait(false);
    }
}

internal sealed record TextRunDto(string Text, double X, double Y, double W, double H);

internal sealed record TextLayerDto(int PageIndex, IReadOnlyList<TextRunDto> Runs)
{
    public static TextLayerDto From(TextLayer layer) =>
        new(layer.PageIndex, [.. layer.Runs.Select(r => new TextRunDto(r.Text, r.X, r.Y, r.W, r.H))]);

    public TextLayer ToTextLayer() =>
        new(PageIndex, [.. Runs.Select(r => new TextRun(r.Text, r.X, r.Y, r.W, r.H))]);
}

[JsonSerializable(typeof(TextLayerDto))]
[JsonSerializable(typeof(TextRunDto))]
internal sealed partial class OcrCacheJsonContext : JsonSerializerContext;
