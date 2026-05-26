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

    /// <summary>
    /// Атомарно (под одной блокировкой) применяет <paramref name="mutate"/> к актуальному
    /// <see cref="Current"/>, сохраняет результат и обновляет <see cref="Current"/>. Единая точка
    /// мутации всех настроек — исключает потерю полей при параллельной записи из разных сервисов
    /// (напр. recents vs тема). Если мутатор вернул тот же экземпляр — запись не выполняется.
    /// </summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct);
}
