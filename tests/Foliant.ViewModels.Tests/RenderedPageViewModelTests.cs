using FluentAssertions;
using Foliant.Domain;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class RenderedPageViewModelTests
{
    private static RenderedPageViewModel Create(int pageIndex, List<FakePageRender> produced, Action? onRender = null)
    {
        return new RenderedPageViewModel(
            pageIndex,
            (_, _, _) =>
            {
                onRender?.Invoke();
                var r = new FakePageRender();
                produced.Add(r);
                return Task.FromResult<IPageRender>(r);
            },
            () => RenderOptions.Default);
    }

    [Fact]
    public async Task EnsureRendered_SetsRender_AndExposesDisplayNumber()
    {
        var produced = new List<FakePageRender>();
        var vm = Create(2, produced);

        await vm.EnsureRenderedAsync(default);

        vm.Render.Should().NotBeNull();
        vm.PageIndex.Should().Be(2);
        vm.DisplayNumber.Should().Be(3);
        produced.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureRendered_IsIdempotent()
    {
        int calls = 0;
        var produced = new List<FakePageRender>();
        var vm = Create(0, produced, () => calls++);

        await vm.EnsureRenderedAsync(default);
        await vm.EnsureRenderedAsync(default);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Invalidate_ClearsAndDisposes_ThenRerenders()
    {
        var produced = new List<FakePageRender>();
        var vm = Create(0, produced);
        await vm.EnsureRenderedAsync(default);

        vm.Invalidate();

        vm.Render.Should().BeNull();
        produced[0].IsDisposed.Should().BeTrue();

        await vm.EnsureRenderedAsync(default);
        vm.Render.Should().NotBeNull();
        produced.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dispose_DisposesRender_AndBlocksFurtherRender()
    {
        var produced = new List<FakePageRender>();
        var vm = Create(0, produced);
        await vm.EnsureRenderedAsync(default);

        vm.Dispose();

        produced[0].IsDisposed.Should().BeTrue();
        await vm.EnsureRenderedAsync(default); // no-op after dispose
        produced.Should().ContainSingle();
    }
}
