using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Search;

/// <summary>
/// Персистирующая реализация <see cref="ISearchHistoryService"/>: хранит историю
/// поиска в JSON-файле (атомарная запись через .tmp + File.Move). При старте
/// приложения вызвать <see cref="LoadAsync"/>; после каждой мутации изменения
/// сохраняются в фоне (fire-and-forget, логируются ошибки).
/// </summary>
public sealed class JsonSearchHistoryService : ISearchHistoryService
{
    private readonly string _filePath;
    private readonly int _maxItems;
    private readonly ILogger<JsonSearchHistoryService> _log;
    private readonly List<string> _items = [];
    private readonly Lock _gate = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        TypeInfoResolver = SearchHistoryJsonContext.Default,
        WriteIndented = false,
    };

    public JsonSearchHistoryService(
        string filePath,
        ILogger<JsonSearchHistoryService> log,
        int maxItems = ISearchHistoryService.DefaultMaxItems)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);
        _filePath = filePath;
        _log = log;
        _maxItems = maxItems;
    }

    /// <summary>Загрузить историю из файла. Если файл отсутствует или повреждён —
    /// стартуем с пустым списком (non-fatal).</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Corrupt history file must not prevent app start; we log and continue empty.")]
    public async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer
                .DeserializeAsync<List<string>>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);

            if (loaded is not null)
            {
                lock (_gate)
                {
                    _items.Clear();
                    _items.AddRange(loaded.Take(_maxItems));
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load search history from {Path}; starting empty.", _filePath);
        }
    }

    public IReadOnlyList<string> GetHistory()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }

    public void Add(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        lock (_gate)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_items[i], query, StringComparison.OrdinalIgnoreCase))
                {
                    _items.RemoveAt(i);
                }
            }
            _items.Insert(0, query);

            while (_items.Count > _maxItems)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        ScheduleSave();
    }

    public void Remove(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            _items.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        }

        ScheduleSave();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SaveCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to persist search history to {Path}.", _filePath);
            }
        });
    }

    private async Task SaveCoreAsync(CancellationToken ct)
    {
        string[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _items];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, ct).ConfigureAwait(false);
        }

        File.Move(tmp, _filePath, overwrite: true);
    }
}

[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class SearchHistoryJsonContext : JsonSerializerContext;
