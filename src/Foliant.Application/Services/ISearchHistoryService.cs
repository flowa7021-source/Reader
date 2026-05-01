namespace Foliant.Application.Services;

/// <summary>
/// App-wide MRU-список поисковых запросов. Сохраняет историю поиска между
/// вкладками и (при наличии персистирующей реализации) между сессиями.
///
/// Реализации: <c>SearchHistoryService</c> (in-memory, потокобезопасная),
/// будущая — <c>JsonSearchHistoryService</c> (персистент на диске).
///
/// Семантика Add: дедуп регистро-нечувствительный (новый запрос «PDF»
/// вытесняет старый «pdf»); cap = <see cref="DefaultMaxItems"/>.
/// </summary>
public interface ISearchHistoryService
{
    /// <summary>Максимальное число записей в истории по умолчанию.</summary>
    const int DefaultMaxItems = 50;

    /// <summary>Получить снимок истории — most-recent first. Потокобезопасно.</summary>
    IReadOnlyList<string> GetHistory();

    /// <summary>Добавить запрос в начало истории (или поднять, если уже есть).
    /// Пустой/whitespace-запрос игнорируется.</summary>
    void Add(string query);

    /// <summary>Удалить все вхождения запроса (регистро-нечувствительно). No-op если нет.</summary>
    void Remove(string query);

    /// <summary>Очистить всю историю.</summary>
    void Clear();
}
