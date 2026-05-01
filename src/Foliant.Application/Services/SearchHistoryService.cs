namespace Foliant.Application.Services;

/// <summary>
/// In-memory потокобезопасная реализация <see cref="ISearchHistoryService"/>.
/// Хранит историю в памяти — при рестарте приложения теряется. Для
/// персистента требуется обёртка с загрузкой/сохранением в файл/БД.
/// </summary>
public sealed class SearchHistoryService : ISearchHistoryService
{
    private readonly List<string> _items = [];
    private readonly Lock _gate = new();
    private readonly int _maxItems;

    /// <param name="maxItems">Максимальная длина истории.
    /// Должен быть > 0; по умолчанию <see cref="ISearchHistoryService.DefaultMaxItems"/>.</param>
    public SearchHistoryService(int maxItems = ISearchHistoryService.DefaultMaxItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);
        _maxItems = maxItems;
    }

    public IReadOnlyList<string> GetHistory()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }

    /// <inheritdoc/>
    public void Add(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        lock (_gate)
        {
            // Регистро-нечувствительный дедуп: удаляем старое вхождение.
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
    }

    /// <inheritdoc/>
    public void Remove(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            _items.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }
}
