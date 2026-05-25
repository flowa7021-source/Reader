using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class SearchHistoryServiceTests
{
    private static SearchHistoryService MakeSut(int maxItems = 10) => new(maxItems);

    // ───── S6/D ─────

    [Fact]
    public void GetHistory_Empty_ReturnsEmptyList()
    {
        var sut = MakeSut();

        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void Add_SingleItem_IsReturnedMostRecentFirst()
    {
        var sut = MakeSut();

        sut.Add("hello");

        sut.GetHistory().Should().Equal(["hello"]);
    }

    [Fact]
    public void Add_MultipleItems_OrderIsMostRecentFirst()
    {
        var sut = MakeSut();

        sut.Add("first");
        sut.Add("second");
        sut.Add("third");

        sut.GetHistory().Should().Equal(["third", "second", "first"]);
    }

    [Fact]
    public void Add_DuplicateCaseInsensitive_PromotesExistingToFront()
    {
        var sut = MakeSut();
        sut.Add("pdf");
        sut.Add("word");
        sut.Add("PDF");   // same as "pdf" — should promote

        sut.GetHistory().Should().Equal(["PDF", "word"]);
    }

    [Fact]
    public void Add_ExceedsMaxItems_OldestDropped()
    {
        var sut = MakeSut(maxItems: 3);

        sut.Add("a");
        sut.Add("b");
        sut.Add("c");
        sut.Add("d");

        sut.GetHistory().Should().Equal(["d", "c", "b"]);
        sut.GetHistory().Should().HaveCount(3);
    }

    [Fact]
    public void Add_WhitespaceOrEmpty_IsIgnored()
    {
        var sut = MakeSut();
        sut.Add("  ");
        sut.Add("");

        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void Remove_ExistingItem_DropsIt()
    {
        var sut = MakeSut();
        sut.Add("alpha");
        sut.Add("beta");

        sut.Remove("alpha");

        sut.GetHistory().Should().Equal(["beta"]);
    }

    [Fact]
    public void Remove_CaseInsensitive_RemovesMatchingCase()
    {
        var sut = MakeSut();
        sut.Add("alpha");
        sut.Add("Alpha");   // same item (was deduped on Add)
        // After two Adds with dedup: only "Alpha" remains at front.

        sut.Remove("ALPHA");

        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void Remove_UnknownItem_IsNoOp()
    {
        var sut = MakeSut();
        sut.Add("x");

        sut.Remove("y");

        sut.GetHistory().Should().Equal(["x"]);
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var sut = MakeSut();
        sut.Add("a");
        sut.Add("b");
        sut.Add("c");

        sut.Clear();

        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void GetHistory_ReturnsSnapshot_NotLiveList()
    {
        var sut = MakeSut();
        sut.Add("x");

        var snapshot = sut.GetHistory();
        sut.Add("y");

        snapshot.Should().Equal(["x"]); // snapshot не изменился
        sut.GetHistory().Should().Equal(["y", "x"]);
    }

    [Fact]
    public void Constructor_NegativeOrZeroMaxItems_Throws()
    {
        var act0 = () => new SearchHistoryService(0);
        var actNeg = () => new SearchHistoryService(-1);

        act0.Should().Throw<ArgumentOutOfRangeException>();
        actNeg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Add_NullArg_Throws()
    {
        var sut = MakeSut();

        var act = () => sut.Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_NullArg_Throws()
    {
        var sut = MakeSut();

        var act = () => sut.Remove(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Add_ConcurrentCalls_DoNotCorruptState()
    {
        var sut = MakeSut(maxItems: ISearchHistoryService.DefaultMaxItems);
        var queries = Enumerable.Range(0, 200).Select(i => $"q{i}").ToArray();

        Parallel.ForEach(queries, q => sut.Add(q));

        // Гарантируем только безопасность — не зависаний, не исключений.
        var history = sut.GetHistory();
        history.Count.Should().BeLessThanOrEqualTo(ISearchHistoryService.DefaultMaxItems);
        history.Should().OnlyHaveUniqueItems();
    }
}
