using BenchmarkDotNet.Attributes;
using Foliant.Infrastructure.Caching;

namespace Foliant.Performance;

[MemoryDiagnoser]
[BenchmarkCategory("CrossPlatform")]
public class LruCacheBenchmarks
{
    private const int Keys = 4096;

    private LruCache<int, byte[]> _cache = null!;
    private byte[] _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = new byte[1024];
        // Capacity holds ~half the working set so Put churns the LRU (eviction path).
        _cache = new LruCache<int, byte[]>(Keys / 2 * _payload.Length, v => v.Length);
        for (var i = 0; i < Keys; i++)
        {
            _cache.Put(i, _payload);
        }
    }

    [Benchmark]
    public long PutWithEviction()
    {
        for (var i = 0; i < Keys; i++)
        {
            _cache.Put(i, _payload);
        }
        return _cache.CurrentBytes;
    }

    [Benchmark]
    public int TryGetHitMix()
    {
        var hits = 0;
        for (var i = 0; i < Keys; i++)
        {
            if (_cache.TryGet(i, out _))
            {
                hits++;
            }
        }
        return hits;
    }
}
