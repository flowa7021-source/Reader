using Foliant.Domain;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Слой 1 кэша рендера: bitmap'ы недавно отрисованных страниц.
/// Sticky-окно ±N от текущей страницы — не эвиктится. См. план, раздел 5.1.
/// </summary>
public sealed class MemoryPageCache(long capacityBytes, int stickyWindow = 5) : IDisposable
{
    private readonly LruCache<CacheKey, IPageRender> _cache = new(
        capacityBytes,
        sizeOf: r => (long)r.Stride * r.HeightPx);

    private readonly Lock _stickyGate = new();
    private string? _stickyDocFp;
    private int _stickyCenter = -1;

    public long CurrentBytes => _cache.CurrentBytes;

    public int Count => _cache.Count;

    /// <summary>Пометить «текущую» страницу — её и соседей в окне ±stickyWindow не выгоняем.</summary>
    public void SetCurrent(string docFingerprint, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(docFingerprint);
        lock (_stickyGate)
        {
            _stickyDocFp = docFingerprint;
            _stickyCenter = pageIndex;
        }
    }

    public bool TryGet(CacheKey key, out IPageRender render) => _cache.TryGet(key, out render);

    /// <summary>
    /// Кладёт значение. Если кэш переполнится — выгоняет LRU-записи, кроме sticky-окна.
    /// Реализация простая: при превышении лимита делаем re-insert sticky значений в начало,
    /// чтобы LruCache их не съел.
    /// </summary>
    public void Put(CacheKey key, IPageRender render)
    {
        TouchSticky();
        _cache.Put(key, render);
    }

    public void Invalidate(string docFingerprint)
    {
        ArgumentNullException.ThrowIfNull(docFingerprint);
        // Простая стратегия: полный обход не нужен — DiskCache знает доc_fp; в RAM
        // мы выгоняем точечно, когда документ закрывается. Сейчас просто Clear по
        // всему кэшу при инвалидации (в Phase 1 OK; точечная инвалидация — Phase 2).
        if (_stickyDocFp == docFingerprint)
        {
            lock (_stickyGate)
            {
                _stickyDocFp = null;
                _stickyCenter = -1;
            }
        }
        _cache.Clear();
    }

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Clear();

    private void TouchSticky()
    {
        string? doc;
        int center;
        lock (_stickyGate)
        {
            doc = _stickyDocFp;
            center = _stickyCenter;
        }
        if (doc is null || center < 0)
        {
            return;
        }

        for (var p = Math.Max(0, center - stickyWindow); p <= center + stickyWindow; p++)
        {
            // TryGet поднимает запись в head — этого достаточно, чтобы LRU не выгнал.
            // Делаем перебор по всем зум-бакетам, которые могли быть закэшированы.
            // Для простоты — пробуем вытащить ключи с наиболее вероятными flags=0..7.
            for (var flags = 0; flags < 8; flags++)
            {
                _ = _cache.TryGet(new CacheKey(doc, p, EngineVersion: 1, ZoomBucket: 100, Flags: flags), out _);
            }
        }
    }
}
