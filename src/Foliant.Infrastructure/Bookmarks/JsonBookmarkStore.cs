using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Bookmarks;

/// <summary>
/// Sidecar-хранилище закладок: per-document JSON-файл, атомарная запись через
/// <c>.tmp</c>+Move, per-document <see cref="SemaphoreSlim"/> для concurrent CRUD.
/// Зеркалит <c>JsonAnnotationStore</c>, но без поля Update — закладки иммутабельны.
/// </summary>
public sealed class JsonBookmarkStore : IBookmarkStore, IDisposable
{
    private readonly string _rootDir;
    private readonly ILogger<JsonBookmarkStore> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public JsonBookmarkStore(string rootDir, ILogger<JsonBookmarkStore> log)
    {
        ArgumentNullException.ThrowIfNull(rootDir);
        ArgumentNullException.ThrowIfNull(log);
        _rootDir = rootDir;
        _log = log;
        Directory.CreateDirectory(_rootDir);
    }

    public async Task<IReadOnlyList<Bookmark>> ListAsync(string docFingerprint, CancellationToken ct)
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

    public async Task AddAsync(string docFingerprint, Bookmark bookmark, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);
        ArgumentNullException.ThrowIfNull(bookmark);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
            var next = new List<Bookmark>(existing) { bookmark };
            await SaveAsync(docFingerprint, next, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(string docFingerprint, Bookmark bookmark, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);
        ArgumentNullException.ThrowIfNull(bookmark);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
            var next = new List<Bookmark>(existing.Count);
            bool replaced = false;
            foreach (var b in existing)
            {
                if (b.Id == bookmark.Id)
                {
                    next.Add(bookmark);
                    replaced = true;
                }
                else
                {
                    next.Add(b);
                }
            }

            if (!replaced)
            {
                throw new KeyNotFoundException($"Bookmark {bookmark.Id} not found for document {docFingerprint}");
            }

            await SaveAsync(docFingerprint, next, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string docFingerprint, Guid bookmarkId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(docFingerprint, ct).ConfigureAwait(false);
            var next = existing.Where(b => b.Id != bookmarkId).ToList();
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
    private async Task<IReadOnlyList<Bookmark>> LoadAsync(string fp, CancellationToken ct)
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
                .DeserializeAsync(stream, BookmarksJsonContext.Default.BookmarksFile, ct)
                .ConfigureAwait(false);
            return dto?.Bookmarks?.Select(FromDto).ToList() ?? (IReadOnlyList<Bookmark>)[];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _log.LogWarning(ex, "Corrupt bookmark sidecar at {Path}; treating as empty", path);
            return [];
        }
    }

    private async Task SaveAsync(string fp, List<Bookmark> items, CancellationToken ct)
    {
        var path = SidecarPath(fp);
        var tmp = path + ".tmp";
        var dto = new BookmarksFile(1, [.. items.Select(ToDto)]);

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer
                .SerializeAsync(stream, dto, BookmarksJsonContext.Default.BookmarksFile, ct)
                .ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static BookmarkDto ToDto(Bookmark b) =>
        new(b.Id, b.PageIndex, b.Label, b.CreatedAt);

    private static Bookmark FromDto(BookmarkDto d) =>
        new(d.Id, d.PageIndex, d.Label, d.CreatedAt);
}

internal sealed record BookmarksFile(int Version, IReadOnlyList<BookmarkDto> Bookmarks);

internal sealed record BookmarkDto(
    Guid Id,
    int PageIndex,
    string Label,
    DateTimeOffset CreatedAt);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BookmarksFile))]
[JsonSerializable(typeof(BookmarkDto))]
internal sealed partial class BookmarksJsonContext : JsonSerializerContext;
