using FluentAssertions;
using Foliant.ViewModels;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class ThumbnailStripViewModelTests
{
    private static Func<int, int, CancellationToken, Task> NoopReorder =>
        (_, _, _) => Task.CompletedTask;

    [Fact]
    public void Constructor_BuildsPages_WithSequentialIndexesAndDisplayNumbers()
    {
        var vm = new ThumbnailStripViewModel(5, NoopReorder, _ => { });

        vm.Pages.Should().HaveCount(5);
        vm.Pages.Select(p => p.PageIndex).Should().Equal(0, 1, 2, 3, 4);
        vm.Pages.Select(p => p.DisplayNumber).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Constructor_NullReorder_Throws()
    {
        var act = () => new ThumbnailStripViewModel(3, null!, _ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOnSelect_Throws()
    {
        var act = () => new ThumbnailStripViewModel(3, NoopReorder, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NegativePageCount_Throws()
    {
        var act = () => new ThumbnailStripViewModel(-1, NoopReorder, _ => { });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SelectedPageIndex_MarksExactlyOnePage_AndInvokesOnSelect()
    {
        int? selected = null;
        var vm = new ThumbnailStripViewModel(4, NoopReorder, i => selected = i);

        vm.SelectedPageIndex = 2;

        selected.Should().Be(2);
        vm.Pages.Count(p => p.IsSelected).Should().Be(1);
        vm.Pages.Single(p => p.IsSelected).PageIndex.Should().Be(2);
    }

    [Fact]
    public void SelectedPageIndex_OutOfRange_DoesNotInvokeOnSelect()
    {
        var onSelect = Substitute.For<Action<int>>();
        var vm = new ThumbnailStripViewModel(3, NoopReorder, onSelect);

        vm.SelectedPageIndex = 99;

        onSelect.DidNotReceive().Invoke(Arg.Any<int>());
        vm.Pages.Should().OnlyContain(p => !p.IsSelected);
    }

    [Fact]
    public async Task MoveAsync_InvokesReorderOnce_ReordersAndRenumbers()
    {
        var reorder = Substitute.For<Func<int, int, CancellationToken, Task>>();
        reorder.Invoke(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var vm = new ThumbnailStripViewModel(5, reorder, _ => { });

        await vm.MoveAsync(0, 3, CancellationToken.None);

        await reorder.Received(1).Invoke(0, 3, Arg.Any<CancellationToken>());
        vm.Pages.Select(p => p.PageIndex).Should().Equal(0, 1, 2, 3, 4);
        vm.Pages.Select(p => p.DisplayNumber).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task MoveAsync_MovingSelectedPage_KeepsSelectionAtNewPosition()
    {
        var vm = new ThumbnailStripViewModel(5, NoopReorder, _ => { });
        vm.SelectedPageIndex = 1;

        await vm.MoveAsync(1, 4, CancellationToken.None);

        vm.SelectedPageIndex.Should().Be(4);
        vm.Pages.Single(p => p.IsSelected).PageIndex.Should().Be(4);
    }

    [Fact]
    public async Task MoveAsync_SameSourceAndTarget_IsNoOp()
    {
        var reorder = Substitute.For<Func<int, int, CancellationToken, Task>>();
        var vm = new ThumbnailStripViewModel(3, reorder, _ => { });

        await vm.MoveAsync(1, 1, CancellationToken.None);

        await reorder.DidNotReceive().Invoke(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 9)]
    [InlineData(5, 1)]
    public async Task MoveAsync_OutOfRange_IsNoOp(int from, int to)
    {
        var reorder = Substitute.For<Func<int, int, CancellationToken, Task>>();
        var vm = new ThumbnailStripViewModel(3, reorder, _ => { });

        await vm.MoveAsync(from, to, CancellationToken.None);

        await reorder.DidNotReceive().Invoke(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        vm.Pages.Select(p => p.PageIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void SetPageCount_RebuildsCollection()
    {
        var vm = new ThumbnailStripViewModel(3, NoopReorder, _ => { });

        vm.SetPageCount(6);

        vm.Pages.Should().HaveCount(6);
        vm.Pages.Select(p => p.DisplayNumber).Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void SetPageCount_ClampsSelectionIntoRange()
    {
        var vm = new ThumbnailStripViewModel(10, NoopReorder, _ => { });
        vm.SelectedPageIndex = 8;

        vm.SetPageCount(4);

        vm.SelectedPageIndex.Should().Be(3);
        vm.Pages.Single(p => p.IsSelected).PageIndex.Should().Be(3);
    }

    [Fact]
    public void SetPageCount_Zero_ClampsSelectionToZero()
    {
        var vm = new ThumbnailStripViewModel(5, NoopReorder, _ => { });
        vm.SelectedPageIndex = 3;

        vm.SetPageCount(0);

        vm.Pages.Should().BeEmpty();
        vm.SelectedPageIndex.Should().Be(0);
    }
}
