namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Слой 2 кэша рендера: миниатюры страниц одного документа. Живёт пока
/// документ открыт — в отличие от <see cref="MemoryPageCache"/>, который
/// глобален. Хранит уже кодированные байты (PNG/JPEG) — UI разворачивает
/// их в <c>BitmapSource</c> по требованию.
///
/// Eviction: нет. Документ редко имеет > 1000 страниц, средняя миниатюра
/// ~5–20 КБ → пиковое потребление ~5–20 МБ, что приемлемо. Закрытие
/// документа = <see cref="Clear"/> = весь GC одной операцией.
///
/// Concurrency: thread-safe для одиночных операций (lock на словаре).
/// </summary>
public sealed class ThumbnailCache
{
    private readonly Dictionary<int, byte[]> _entries = new();
    private readonly Lock _gate = new();
    private long _totalBytes;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public bool TryGet(int pageIndex, out byte[] thumb)
    {
        if (pageIndex < 0)
        {
            thumb = [];
            return false;
        }
        lock (_gate)
        {
            if (_entries.TryGetValue(pageIndex, out var value))
            {
                thumb = value;
                return true;
            }
        }
        thumb = [];
        return false;
    }

    /// <summary>Сохранить миниатюру. Если страница уже была закэширована,
    /// предыдущие байты заменяются (например, после reflow / page resize).</summary>
    public void Put(int pageIndex, byte[] thumb)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentNullException.ThrowIfNull(thumb);
        lock (_gate)
        {
            if (_entries.TryGetValue(pageIndex, out var existing))
            {
                _totalBytes -= existing.Length;
            }
            _entries[pageIndex] = thumb;
            _totalBytes += thumb.Length;
        }
    }

    public bool Remove(int pageIndex)
    {
        lock (_gate)
        {
            if (_entries.Remove(pageIndex, out var existing))
            {
                _totalBytes -= existing.Length;
                return true;
            }
        }
        return false;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _totalBytes = 0;
        }
    }
}
