using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Annotations;

/// <summary>
/// Sidecar-хранилище аннотаций в JSON. Per-document файл
/// <c>{rootDir}/{fingerprint}.json</c>. Запись атомарная (через <c>.tmp</c> + Move).
/// Конкурентные операции по одному документу сериализуются через <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class JsonAnnotationStore : IAnnotationStore, IDisposable
{
    private readonly string _rootDir;
    private readonly ILogger<JsonAnnotationStore> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public JsonAnnotationStore(string rootDir, ILogger<JsonAnnotationStore> log)
    {
        ArgumentNullException.ThrowIfNull(rootDir);
        ArgumentNullException.ThrowIfNull(log);
        _rootDir = rootDir;
        _log = log;
        Directory.CreateDirectory(_rootDir);
    }

    public async Task<IReadOnlyList<Annotation>> ListAsync(string docFingerprint, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AddAsync(string docFingerprint, Annotation annotation, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);
        ArgumentNullException.ThrowIfNull(annotation);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
            var next = new List<Annotation>(existing) { annotation };
            await SaveAsync(docFingerprint, next, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(string docFingerprint, Annotation annotation, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);
        ArgumentNullException.ThrowIfNull(annotation);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
            var next = new List<Annotation>(existing.Count);
            var replaced = false;
            foreach (var a in existing)
            {
                if (a.Id == annotation.Id)
                {
                    next.Add(annotation);
                    replaced = true;
                }
                else
                {
                    next.Add(a);
                }
            }

            if (!replaced)
            {
                throw new KeyNotFoundException($"Annotation {annotation.Id} not found for document {docFingerprint}");
            }

            await SaveAsync(docFingerprint, next, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string docFingerprint, Guid annotationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
            var next = existing.Where(a => a.Id != annotationId).ToList();
            if (next.Count == existing.Count)
            {
                return false;
            }
            await SaveAsync(docFingerprint, next, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAllAsync(string docFingerprint, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = SidecarPath(docFingerprint);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (var s in _gates.Values)
        {
            s.Dispose();
        }
        _gates.Clear();
    }

    private SemaphoreSlim GetGate(string fp) =>
        _gates.GetOrAdd(fp, _ => new SemaphoreSlim(1, 1));

    private string SidecarPath(string fp) =>
        Path.Combine(_rootDir, fp + ".json");

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Corrupt sidecar must not block the user; we log + treat as empty.")]
    private async Task<IReadOnlyList<Annotation>> LoadAsync(string fp, CancellationToken ct)
    {
        var path = SidecarPath(fp);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var dto = await JsonSerializer
                .DeserializeAsync(stream, AnnotationsJsonContext.Default.AnnotationsFile, ct)
                .ConfigureAwait(false);
            return dto?.Annotations?.Select(FromDto).ToList() ?? (IReadOnlyList<Annotation>)[];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _log.LogWarning(ex, "Corrupt annotation sidecar at {Path}; treating as empty", path);
            return [];
        }
    }

    private async Task SaveAsync(string fp, IReadOnlyList<Annotation> items, CancellationToken ct)
    {
        var path = SidecarPath(fp);
        var tmp = path + ".tmp";
        var dto = new AnnotationsFile(1, [.. items.Select(ToDto)]);

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer
                .SerializeAsync(stream, dto, AnnotationsJsonContext.Default.AnnotationsFile, ct)
                .ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static AnnotationDto ToDto(Annotation a) =>
        new(a.Id, a.PageIndex, a.Kind,
            a.ColorHex,
            a.Bounds is null ? null : new RectDto(a.Bounds.X, a.Bounds.Y, a.Bounds.Width, a.Bounds.Height),
            a.Text,
            a.InkPoints is null ? null : [.. a.InkPoints.Select(p => new PointDto(p.X, p.Y))],
            a.CreatedAt);

    private static Annotation FromDto(AnnotationDto d) =>
        new(d.Id, d.PageIndex, d.Kind, d.ColorHex,
            d.Bounds is null ? null : new AnnotationRect(d.Bounds.X, d.Bounds.Y, d.Bounds.Width, d.Bounds.Height),
            d.Text,
            d.InkPoints is null ? null : [.. d.InkPoints.Select(p => new AnnotationPoint(p.X, p.Y))],
            d.CreatedAt);
}

internal sealed record AnnotationsFile(int Version, IReadOnlyList<AnnotationDto> Annotations);

internal sealed record AnnotationDto(
    Guid Id,
    int PageIndex,
    AnnotationKind Kind,
    string ColorHex,
    RectDto? Bounds,
    string? Text,
    IReadOnlyList<PointDto>? InkPoints,
    DateTimeOffset CreatedAt);

internal sealed record RectDto(double X, double Y, double Width, double Height);

internal sealed record PointDto(double X, double Y);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotationsFile))]
[JsonSerializable(typeof(AnnotationDto))]
[JsonSerializable(typeof(RectDto))]
[JsonSerializable(typeof(PointDto))]
internal sealed partial class AnnotationsJsonContext : JsonSerializerContext;
