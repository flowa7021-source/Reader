using Foliant.Application.Services;
using Foliant.Application.Settings;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Settings;

/// <summary>
/// MRU-список последних открытых документов. Все мутации идут через единую точку
/// <see cref="ISettingsService.UpdateAsync"/> — это и сериализует запись, и сохраняет
/// остальные поля <see cref="AppSettings"/> (тему, кэш, OCR) при параллельной записи
/// из разных сервисов (исключает lost-update поверх ISettingsStore напрямую).
/// </summary>
public sealed class RecentsService : IRecentsService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<RecentsService> _log;

    public RecentsService(ISettingsService settings, ILogger<RecentsService> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
    }

    public Task<IReadOnlyList<string>> GetAsync(CancellationToken ct)
        => Task.FromResult(_settings.Current.RecentFiles);

    public async Task AddAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string canonical = NormalizePath(path);

        await _settings.UpdateAsync(current =>
        {
            List<string> updated = MoveToFrontAndCap(current.RecentFiles, canonical);
            if (updated.SequenceEqual(current.RecentFiles, StringComparer.OrdinalIgnoreCase))
            {
                return current;
            }

            _log.LogDebug("Recents: added {Path}; size={Size}", canonical, updated.Count);
            return current with { RecentFiles = updated };
        }, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string canonical = NormalizePath(path);

        await _settings.UpdateAsync(current =>
        {
            var filtered = current.RecentFiles
                .Where(p => !PathEquals(p, canonical))
                .ToArray();

            if (filtered.Length == current.RecentFiles.Count)
            {
                return current;
            }

            _log.LogDebug("Recents: removed {Path}; size={Size}", canonical, filtered.Length);
            return current with { RecentFiles = filtered };
        }, ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _settings.UpdateAsync(current =>
        {
            if (current.RecentFiles.Count == 0)
            {
                return current;
            }

            _log.LogDebug("Recents: cleared");
            return current with { RecentFiles = [] };
        }, ct).ConfigureAwait(false);
    }

    private static List<string> MoveToFrontAndCap(IReadOnlyList<string> existing, string path)
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
