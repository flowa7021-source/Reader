namespace Foliant.Application.Services;

/// <summary>
/// Список недавно открытых документов (MRU). Перезаписывается через ISettingsStore;
/// дедуплицирует по полному пути (case-insensitive), кэп — <see cref="MaxItems"/>.
/// </summary>
public interface IRecentsService
{
    /// <summary>Максимум элементов, которые сервис хранит. См. план: «Recents показывает последние 20 файлов».</summary>
    const int MaxItems = 20;

    Task<IReadOnlyList<string>> GetAsync(CancellationToken ct);

    Task AddAsync(string path, CancellationToken ct);

    Task RemoveAsync(string path, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
