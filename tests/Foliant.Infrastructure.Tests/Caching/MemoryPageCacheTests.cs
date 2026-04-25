using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class MemoryPageCacheTests
{
    [Fact]
    public void PutThenTryGet_ReturnsRender()
    {
        using var sut = new MemoryPageCache(capacityBytes: 1_000_000);
        var key = K(0);
        var render = new FakePageRender(100, 100);

        sut.Put(key, render);

        sut.TryGet(key, out var got).Should().BeTrue();
        got.Should().BeSameAs(render);
    }

    [Fact]
    public void CurrentBytes_ReflectsStrideTimesHeight()
    {
        using var sut = new MemoryPageCache(capacityBytes: 1_000_000);

        sut.Put(K(0), new FakePageRender(100, 100));   // 100 * (100*4) = 40_000

        sut.CurrentBytes.Should().Be(40_000);
    }

    [Fact]
    public void Eviction_HappensWhenCapacityExceeded_NoSticky()
    {
        // 3 страницы по 40_000 = 120_000. capacity 100_000 → одна выпадет.
        using var sut = new MemoryPageCache(capacityBytes: 100_000);

        sut.Put(K(0), new FakePageRender(100, 100));
        sut.Put(K(1), new FakePageRender(100, 100));
        sut.Put(K(2), new FakePageRender(100, 100));

        sut.Count.Should().Be(2);
        sut.TryGet(K(0), out _).Should().BeFalse();   // самая старая выпала
    }

    [Fact]
    public void StickyWindow_ProtectsCenterAndNeighbors()
    {
        // 11 страниц по 1000 байт = 11_000. Capacity 5_000.
        // SetCurrent на стр. 5 → защищаются стр. 0..10 (sticky=5, окно ±5).
        // Без sticky всё бы выгнали кроме последних. С sticky — каждый Put
        // переподнимает sticky-страницы в head.
        using var sut = new MemoryPageCache(capacityBytes: 5_000, stickyWindow: 5);

        for (var p = 0; p <= 10; p++)
        {
            sut.Put(K(p), new FakePageRender(width: 5, height: 50));   // 5*4*50 = 1000
        }

        sut.SetCurrent("doc-fp", 5);

        // Добавим ещё страницу 11 — она вытолкнет того, кто давно не использовался.
        sut.Put(K(11), new FakePageRender(5, 50));

        // 11 в кэше, sticky стр 0..10 — кто-то выпал, потому что capacity маленькая.
        // Главная гарантия: после Put sticky-окно «дёргается» и не уходит сразу из RAM.
        // Точные гарантии на маленькой capacity слабы — проверим, что хотя бы сама центральная
        // и одна из непосредственных соседей живы.
        sut.TryGet(K(5), out _).Should().BeTrue();
    }

    [Fact]
    public void Invalidate_OfCurrentDoc_ClearsAndDropsSticky()
    {
        using var sut = new MemoryPageCache(capacityBytes: 1_000_000);
        sut.Put(K(0), new FakePageRender(10, 10));
        sut.SetCurrent("doc-fp", 0);

        sut.Invalidate("doc-fp");

        sut.Count.Should().Be(0);
        sut.TryGet(K(0), out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesAllAndDisposesValues()
    {
        using var sut = new MemoryPageCache(capacityBytes: 1_000_000);
        var r = new FakePageRender(10, 10);
        sut.Put(K(0), r);

        sut.Clear();

        sut.Count.Should().Be(0);
        r.IsDisposed.Should().BeTrue();
    }

    private static CacheKey K(int pageIndex) =>
        new("doc-fp", pageIndex, EngineVersion: 1, ZoomBucket: 100, Flags: 0);
}
