using FluentAssertions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Pure unit tests for <see cref="PageOrder"/> — index math only, no native/PdfPig
/// dependency, so these run everywhere. All outputs are 1-based page numbers.
/// </summary>
public sealed class PageOrderTests
{
    [Theory]
    [InlineData(3, 0, new[] { 2, 3 })]
    [InlineData(3, 1, new[] { 1, 3 })]
    [InlineData(3, 2, new[] { 1, 2 })]
    [InlineData(1, 0, new int[0])]
    public void BuildAfterDelete_RemovesPage_ShiftsRest(int pageCount, int deleteIndex, int[] expected) =>
        PageOrder.BuildAfterDelete(pageCount, deleteIndex).Should().Equal(expected);

    [Theory]
    [InlineData(3, -1)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    public void BuildAfterDelete_BadIndex_Throws(int pageCount, int deleteIndex)
    {
        var act = () => PageOrder.BuildAfterDelete(pageCount, deleteIndex);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildAfterDelete_ZeroPageCount_Throws()
    {
        var act = () => PageOrder.BuildAfterDelete(0, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(3, new[] { 2, 0, 1 }, new[] { 3, 1, 2 })]
    [InlineData(3, new[] { 0, 1, 2 }, new[] { 1, 2, 3 })]
    [InlineData(2, new[] { 1, 0 }, new[] { 2, 1 })]
    [InlineData(1, new[] { 0 }, new[] { 1 })]
    public void BuildReorder_Permutes_To1Based(int pageCount, int[] newOrder, int[] expected) =>
        PageOrder.BuildReorder(pageCount, newOrder).Should().Equal(expected);

    [Fact]
    public void BuildReorder_NullOrder_Throws()
    {
        var act = () => PageOrder.BuildReorder(3, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(3, new[] { 0, 1 })]      // too short
    [InlineData(3, new[] { 0, 1, 2, 0 })] // too long
    public void BuildReorder_WrongLength_Throws(int pageCount, int[] newOrder)
    {
        var act = () => PageOrder.BuildReorder(pageCount, newOrder);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(3, new[] { 0, 0, 1 })]   // duplicate
    [InlineData(3, new[] { 0, 1, 1 })]   // duplicate
    public void BuildReorder_Duplicate_Throws(int pageCount, int[] newOrder)
    {
        var act = () => PageOrder.BuildReorder(pageCount, newOrder);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(3, new[] { 0, 1, 3 })]   // out of range high
    [InlineData(3, new[] { -1, 1, 2 })]  // out of range low
    public void BuildReorder_OutOfRangeMember_Throws(int pageCount, int[] newOrder)
    {
        var act = () => PageOrder.BuildReorder(pageCount, newOrder);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(3, 0, 0)]
    [InlineData(3, 3, 3)] // inclusive end (append)
    [InlineData(3, 2, 2)]
    [InlineData(0, 0, 0)]
    public void ResolveInsertPosition_Valid_ReturnsIndex(int pageCount, int atIndex, int expected) =>
        PageOrder.ResolveInsertPosition(pageCount, atIndex).Should().Be(expected);

    [Theory]
    [InlineData(3, -1)]
    [InlineData(3, 4)]
    public void ResolveInsertPosition_BadIndex_Throws(int pageCount, int atIndex)
    {
        var act = () => PageOrder.ResolveInsertPosition(pageCount, atIndex);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(3, 0, new int[0], new[] { 1, 2, 3 })]   // insert at start
    [InlineData(3, 1, new[] { 1 }, new[] { 2, 3 })]      // after page 1
    [InlineData(3, 3, new[] { 1, 2, 3 }, new int[0])]    // append at end
    public void BasePagesBeforeAndAfter_SplitAroundInsert(
        int pageCount, int atIndex, int[] expectedBefore, int[] expectedAfter)
    {
        PageOrder.BasePagesBefore(pageCount, atIndex).Should().Equal(expectedBefore);
        PageOrder.BasePagesAfter(pageCount, atIndex).Should().Equal(expectedAfter);
    }

    [Theory]
    [InlineData(0, new int[0])]
    [InlineData(1, new[] { 1 })]
    [InlineData(3, new[] { 1, 2, 3 })]
    public void AllPages_Yields1BasedRange(int count, int[] expected) =>
        PageOrder.AllPages(count).Should().Equal(expected);

    [Fact]
    public void BasePagesBefore_PlusAfter_CoverAllPages_NoGap()
    {
        const int pageCount = 5;
        for (int at = 0; at <= pageCount; at++)
        {
            var combined = PageOrder.BasePagesBefore(pageCount, at)
                .Concat(PageOrder.BasePagesAfter(pageCount, at))
                .ToArray();

            combined.Should().Equal(Enumerable.Range(1, pageCount),
                "insert split at {0} must preserve every base page exactly once in order", at);
        }
    }
}
