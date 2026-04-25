namespace Foliant.Infrastructure.Settings;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct);

    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
