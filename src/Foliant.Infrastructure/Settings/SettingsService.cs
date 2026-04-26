using Foliant.Application.Services;
using Foliant.Application.Settings;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Settings;

/// <summary>
/// Кэширует последний загруженный/сохранённый <see cref="AppSettings"/> в памяти.
/// Concurrent-safe: операции записи сериализованы через <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly ILogger<SettingsService> _log;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private AppSettings _current = AppSettings.Default;

    public SettingsService(ISettingsStore store, ILogger<SettingsService> log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _log = log;
    }

    public AppSettings Current => _current;

    public async Task LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _current = await _store.LoadAsync(ct).ConfigureAwait(false);
            _log.LogDebug("Settings loaded: Theme={Theme}, Language={Language}", _current.Theme, _current.Language);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(settings, ct).ConfigureAwait(false);
            _current = settings;
            _log.LogDebug("Settings saved: Theme={Theme}, Language={Language}", settings.Theme, settings.Language);
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
}
