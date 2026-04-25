using FluentAssertions;
using Foliant.Infrastructure.Caching;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class LruCacheTests
{
    [Fact]
    public void Put_ThenTryGet_ReturnsValue()
    {
        var c = NewCache(capacityBytes: 100);

        c.Put("a", "x");

        c.TryGet("a", out var v).Should().BeTrue();
        v.Should().Be("x");
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        var c = NewCache(capacityBytes: 100);

        c.TryGet("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Eviction_HappensWhenCapacityExceeded()
    {
        var c = NewCache(capacityBytes: 30);

        c.Put("a", "1234567890");  // 10
        c.Put("b", "1234567890");  // 10
        c.Put("c", "1234567890");  // 10
        c.Put("d", "1234567890");  // → evicts "a"

        c.TryGet("a", out _).Should().BeFalse();
        c.TryGet("b", out _).Should().BeTrue();
        c.TryGet("c", out _).Should().BeTrue();
        c.TryGet("d", out _).Should().BeTrue();
        c.CurrentBytes.Should().Be(30);
    }

    [Fact]
    public void TryGet_PromotesEntry_PreventingItsEviction()
    {
        var c = NewCache(capacityBytes: 30);

        c.Put("a", "1234567890");
        c.Put("b", "1234567890");
        c.Put("c", "1234567890");
        c.TryGet("a", out _);  // promote
        c.Put("d", "1234567890");  // должен выгнать самый старый — "b"

        c.TryGet("a", out _).Should().BeTrue();
        c.TryGet("b", out _).Should().BeFalse();
    }

    [Fact]
    public void Put_OverwriteSameKey_DoesNotInflateCount()
    {
        var c = NewCache(capacityBytes: 100);

        c.Put("a", "1");
        c.Put("a", "2");

        c.Count.Should().Be(1);
        c.TryGet("a", out var v).Should().BeTrue();
        v.Should().Be("2");
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var c = NewCache(capacityBytes: 100);
        c.Put("a", "1");

        c.Remove("a").Should().BeTrue();
        c.TryGet("a", out _).Should().BeFalse();
        c.CurrentBytes.Should().Be(0);
    }

    [Fact]
    public void Clear_DisposesAllValues_AndResetsBytes()
    {
        var c = new LruCache<string, Disposable>(capacityBytes: 100, sizeOf: _ => 10);
        var d = new Disposable();
        c.Put("a", d);

        c.Clear();

        c.Count.Should().Be(0);
        c.CurrentBytes.Should().Be(0);
        d.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Eviction_DisposesIDisposableValues()
    {
        var c = new LruCache<int, Disposable>(capacityBytes: 20, sizeOf: _ => 10);
        var first = new Disposable();
        var second = new Disposable();
        var third = new Disposable();

        c.Put(1, first);
        c.Put(2, second);
        c.Put(3, third);   // evicts first

        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeFalse();
        third.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        var act = () => new LruCache<string, string>(capacityBytes: 0, sizeOf: _ => 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static LruCache<string, string> NewCache(long capacityBytes) =>
        new(capacityBytes, v => v.Length);

    private sealed class Disposable : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
