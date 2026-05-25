using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class TextStructureCacheTests
{
    private static TextLayer MakeLayer(int pageIndex, params string[] words)
    {
        var runs = words.Select(w => new TextRun(w, 0, 0, 1, 1)).ToArray();
        return new TextLayer(pageIndex, runs);
    }

    [Fact]
    public void Empty_ConstructionState()
    {
        var sut = new TextStructureCache();
        sut.Count.Should().Be(0);
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse_AndEmptyLayer()
    {
        var sut = new TextStructureCache();

        sut.TryGet(0, out var layer).Should().BeFalse();
        layer.Runs.Should().BeEmpty();
    }

    [Fact]
    public void Put_Then_TryGet_RoundtripsLayer()
    {
        var sut = new TextStructureCache();
        var layer = MakeLayer(3, "hello", " world");

        sut.Put(3, layer);

        sut.TryGet(3, out var got).Should().BeTrue();
        got.Should().BeSameAs(layer);
    }

    [Fact]
    public void Put_TwiceSameKey_Overwrites()
    {
        var sut = new TextStructureCache();
        sut.Put(0, MakeLayer(0, "first"));
        sut.Put(0, MakeLayer(0, "second"));

        sut.Count.Should().Be(1);
        sut.TryGet(0, out var got).Should().BeTrue();
        got.ToPlainText().Should().Be("second");
    }

    [Fact]
    public void Put_MismatchedPageIndex_Throws()
    {
        var sut = new TextStructureCache();
        var layer = MakeLayer(5, "x");

        var act = () => sut.Put(3, layer);

        act.Should().Throw<ArgumentException>().WithParameterName("layer");
    }

    [Fact]
    public void Put_NegativeKey_Throws()
    {
        var sut = new TextStructureCache();

        var act = () => sut.Put(-1, MakeLayer(0));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Put_NullLayer_Throws()
    {
        var sut = new TextStructureCache();

        var act = () => sut.Put(0, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var sut = new TextStructureCache();
        sut.Put(0, MakeLayer(0));
        sut.Put(1, MakeLayer(1));

        sut.Remove(0).Should().BeTrue();

        sut.Count.Should().Be(1);
        sut.TryGet(0, out _).Should().BeFalse();
    }

    [Fact]
    public void Remove_Missing_ReturnsFalse()
    {
        var sut = new TextStructureCache();
        sut.Remove(42).Should().BeFalse();
    }

    [Fact]
    public void Clear_DropsAll()
    {
        var sut = new TextStructureCache();
        sut.Put(0, MakeLayer(0));
        sut.Put(1, MakeLayer(1));
        sut.Put(2, MakeLayer(2));

        sut.Clear();

        sut.Count.Should().Be(0);
    }
}
