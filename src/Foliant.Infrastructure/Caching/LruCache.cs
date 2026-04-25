namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Потокобезопасный LRU-кэш с capacity, заданным в байтах. Эвикция — при превышении
/// capacity. Используется как слой 1 (page bitmap cache); см. IMPLEMENTATION_PLAN.md, раздел 5.1.
/// </summary>
/// <typeparam name="TKey">Ключ. Должен корректно реализовывать GetHashCode/Equals.</typeparam>
/// <typeparam name="TValue">Значение. Если IDisposable — будет диспозиться при эвикции.</typeparam>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly long _capacityBytes;
    private readonly Func<TValue, long> _sizeOf;
    private readonly LinkedList<Entry> _order = [];
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = [];
    private readonly Lock _gate = new();
    private long _currentBytes;

    public LruCache(long capacityBytes, Func<TValue, long> sizeOf)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacityBytes, 0);
        ArgumentNullException.ThrowIfNull(sizeOf);

        _capacityBytes = capacityBytes;
        _sizeOf = sizeOf;
    }

    public long CurrentBytes
    {
        get { lock (_gate) { return _currentBytes; } }
    }

    public int Count
    {
        get { lock (_gate) { return _map.Count; } }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Put(TKey key, TValue value)
    {
        var size = _sizeOf(value);
        var evicted = new List<TValue>();

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _currentBytes -= existing.Value.Size;
                _order.Remove(existing);
                evicted.Add(existing.Value.Value);
                _map.Remove(key);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value, size));
            _order.AddFirst(node);
            _map[key] = node;
            _currentBytes += size;

            while (_currentBytes > _capacityBytes && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                _currentBytes -= last.Value.Size;
                evicted.Add(last.Value.Value);
            }
        }

        DisposeEvicted(evicted);
    }

    public bool Remove(TKey key)
    {
        TValue? evicted = default;
        var found = false;
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _currentBytes -= node.Value.Size;
                evicted = node.Value.Value;
                _map.Remove(key);
                found = true;
            }
        }
        if (found && evicted is IDisposable d) d.Dispose();
        return found;
    }

    public void Clear()
    {
        List<TValue> evicted;
        lock (_gate)
        {
            evicted = [.. _order.Select(e => e.Value)];
            _order.Clear();
            _map.Clear();
            _currentBytes = 0;
        }
        DisposeEvicted(evicted);
    }

    private static void DisposeEvicted(List<TValue> values)
    {
        foreach (var v in values)
        {
            if (v is IDisposable d) d.Dispose();
        }
    }

    private readonly record struct Entry(TKey Key, TValue Value, long Size);
}
