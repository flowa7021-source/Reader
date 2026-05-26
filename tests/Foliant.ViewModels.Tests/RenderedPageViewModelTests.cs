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

    private static RenderedPageViewModel CreateWithHighlights(
        int pageIndex,
        Func<int, IReadOnlyList<AnnotationRect>> highlights)
    {
        return new RenderedPageViewModel(
            pageIndex,
            (_, _, _) => Task.FromResult<IPageRender>(new FakePageRender()),
            () => RenderOptions.Default,
            (i, _) => Task.FromResult(highlights(i)));
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

    private static readonly AnnotationRect SampleRect = new(10, 20, 30, 40);

    [Fact]
    public async Task EnsureRendered_WithHighlightProvider_PopulatesSearchHighlights()
    {
        var vm = CreateWithHighlights(1, _ => [SampleRect]);

        await vm.EnsureRenderedAsync(default);

        vm.SearchHighlights.Should().ContainSingle().Which.Should().Be(SampleRect);
    }

    [Fact]
    public async Task NoHighlightProvider_SearchHighlightsStayEmpty()
    {
        var produced = new List<FakePageRender>();
        var vm = Create(0, produced); // 3-arg ctor — no provider

        await vm.EnsureRenderedAsync(default);

        vm.SearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshHighlights_PicksUpProviderChange()
    {
        IReadOnlyList<AnnotationRect> current = [];
        var vm = CreateWithHighlights(0, _ => current);
        await vm.EnsureRenderedAsync(default);
        vm.SearchHighlights.Should().BeEmpty();

        current = [SampleRect];
        await vm.RefreshHighlightsAsync(default);

        vm.SearchHighlights.Should().ContainSingle().Which.Should().Be(SampleRect);
    }

    [Fact]
    public async Task Invalidate_ClearsSearchHighlights()
    {
        var vm = CreateWithHighlights(0, _ => [SampleRect]);
        await vm.EnsureRenderedAsync(default);
        vm.SearchHighlights.Should().ContainSingle();

        vm.Invalidate();

        vm.SearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_ClearsSearchHighlights_AndBlocksRefresh()
    {
        var vm = CreateWithHighlights(0, _ => [SampleRect]);
        await vm.EnsureRenderedAsync(default);

        vm.Dispose();
        vm.SearchHighlights.Should().BeEmpty();

        await vm.RefreshHighlightsAsync(default); // no-op after dispose
        vm.SearchHighlights.Should().BeEmpty();
    }
}
