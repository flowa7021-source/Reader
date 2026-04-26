using Foliant.Application.Settings;

namespace Foliant.Application.Services;

/// <summary>
/// Кэшированный доступ к настройкам приложения. Позволяет получить текущий снимок
/// и сохранить изменённый, не обращаясь напрямую к <see cref="ISettingsStore"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>Текущий снимок настроек (закэширован после последнего LoadAsync/SaveAsync).</summary>
    AppSettings Current { get; }

    /// <summary>Загружает настройки из хранилища и обновляет <see cref="Current"/>.</summary>
    Task LoadAsync(CancellationToken ct);

    /// <summary>
    /// Сохраняет <paramref name="settings"/> в хранилище и обновляет <see cref="Current"/>.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
