using FluentAssertions;
using Foliant.Infrastructure.Caching;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class ThumbnailCacheTests
{
    [Fact]
    public void Empty_ConstructionState()
    {
        var sut = new ThumbnailCache();
        sut.Count.Should().Be(0);
        sut.TotalBytes.Should().Be(0);
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        var sut = new ThumbnailCache();

        sut.TryGet(0, out var thumb).Should().BeFalse();
        thumb.Should().BeEmpty();
    }

    [Fact]
    public void Put_Then_TryGet_RoundtripsBytes()
    {
        var sut = new ThumbnailCache();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        sut.Put(7, bytes);

        sut.TryGet(7, out var got).Should().BeTrue();
        got.Should().BeSameAs(bytes);
    }

    [Fact]
    public void Put_TwiceSameKey_OverwritesAndAdjustsTotalBytes()
    {
        var sut = new ThumbnailCache();
        sut.Put(0, new byte[10]);
        sut.TotalBytes.Should().Be(10);

        sut.Put(0, new byte[3]);

        sut.Count.Should().Be(1);
        sut.TotalBytes.Should().Be(3);
    }

    [Fact]
    public void Put_MultiplePages_AccumulatesTotalBytes()
    {
        var sut = new ThumbnailCache();
        sut.Put(0, new byte[100]);
        sut.Put(1, new byte[50]);
        sut.Put(2, new byte[25]);

        sut.Count.Should().Be(3);
        sut.TotalBytes.Should().Be(175);
    }

    [Fact]
    public void Remove_Existing_DropsAndReducesBytes()
    {
        var sut = new ThumbnailCache();
        sut.Put(0, new byte[100]);
        sut.Put(1, new byte[50]);

        sut.Remove(0).Should().BeTrue();

        sut.Count.Should().Be(1);
        sut.TotalBytes.Should().Be(50);
        sut.TryGet(0, out _).Should().BeFalse();
    }

    [Fact]
    public void Remove_Missing_ReturnsFalse()
    {
        var sut = new ThumbnailCache();
        sut.Remove(42).Should().BeFalse();
    }

    [Fact]
    public void Clear_DropsAll_ZeroesBytes()
    {
        var sut = new ThumbnailCache();
        sut.Put(0, new byte[100]);
        sut.Put(1, new byte[50]);

        sut.Clear();

        sut.Count.Should().Be(0);
        sut.TotalBytes.Should().Be(0);
    }

    [Fact]
    public void Put_NegativePageIndex_Throws()
    {
        var sut = new ThumbnailCache();
        var act = () => sut.Put(-1, [1, 2, 3]);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Put_NullBytes_Throws()
    {
        var sut = new ThumbnailCache();
        var act = () => sut.Put(0, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryGet_NegativeKey_ReturnsFalse_NoThrow()
    {
        var sut = new ThumbnailCache();
        sut.TryGet(-5, out var thumb).Should().BeFalse();
        thumb.Should().BeEmpty();
    }
}
