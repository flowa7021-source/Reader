using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Settings;

/// <summary>
/// MRU-список последних открытых документов, персистится через <see cref="ISettingsStore"/>.
/// Concurrent-safe (внутренний <see cref="SemaphoreSlim"/>): сериализует операции записи,
/// чтобы read-modify-write через ISettingsStore не терял обновления при гонке.
/// </summary>
public sealed class RecentsService : IRecentsService, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly ILogger<RecentsService> _log;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    public RecentsService(ISettingsStore store, ILogger<RecentsService> log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _log = log;
    }

    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken ct)
    {
        AppSettings settings = await _store.LoadAsync(ct).ConfigureAwait(false);
        return settings.RecentFiles;
    }

    public async Task AddAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string canonical = NormalizePath(path);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AppSettings settings = await _store.LoadAsync(ct).ConfigureAwait(false);
            IReadOnlyList<string> updated = MoveToFrontAndCap(settings.RecentFiles, canonical);

            if (SequenceEquals(updated, settings.RecentFiles))
            {
                return;
            }

            await _store.SaveAsync(settings with { RecentFiles = updated }, ct).ConfigureAwait(false);
            _log.LogDebug("Recents: added {Path}; size={Size}", canonical, updated.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string canonical = NormalizePath(path);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AppSettings settings = await _store.LoadAsync(ct).ConfigureAwait(false);
            var filtered = settings.RecentFiles
                .Where(p => !PathEquals(p, canonical))
                .ToArray();

            if (filtered.Length == settings.RecentFiles.Count)
            {
                return;
            }

            await _store.SaveAsync(settings with { RecentFiles = filtered }, ct).ConfigureAwait(false);
            _log.LogDebug("Recents: removed {Path}; size={Size}", canonical, filtered.Length);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AppSettings settings = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (settings.RecentFiles.Count == 0)
            {
                return;
            }

            await _store.SaveAsync(settings with { RecentFiles = [] }, ct).ConfigureAwait(false);
            _log.LogDebug("Recents: cleared");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private static IReadOnlyList<string> MoveToFrontAndCap(IReadOnlyList<string> existing, string path)
    {
        var list = new List<string>(Math.Min(existing.Count + 1, IRecentsService.MaxItems))
        {
            path,
        };

        foreach (string p in existing)
        {
            if (list.Count >= IRecentsService.MaxItems)
            {
                break;
            }
            if (!PathEquals(p, path))
            {
                list.Add(p);
            }
        }

        return list;
    }

    private static bool SequenceEquals(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        for (int i = 0; i < a.Count; i++)
        {
            if (!PathEquals(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
    }
}
