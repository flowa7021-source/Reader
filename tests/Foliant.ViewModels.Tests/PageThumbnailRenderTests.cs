using FluentAssertions;
using Foliant.Domain;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class PageThumbnailRenderTests
{
    private static Func<int, CancellationToken, Task<IPageRender>> Renderer(List<FakePageRender> produced, Action? onRender = null)
        => (_, _) =>
        {
            onRender?.Invoke();
            var r = new FakePageRender();
            produced.Add(r);
            return Task.FromResult<IPageRender>(r);
        };

    [Fact]
    public async Task EnsureThumbnail_RendersOnce_ViaDelegate()
    {
        int calls = 0;
        var produced = new List<FakePageRender>();
        var vm = new PageThumbnailViewModel(2, Renderer(produced, () => calls++));

        await vm.EnsureThumbnailAsync(default);
        await vm.EnsureThumbnailAsync(default);

        vm.Thumbnail.Should().NotBeNull();
        calls.Should().Be(1);
        vm.PageIndex.Should().Be(2);
        vm.DisplayNumber.Should().Be(3);
    }

    [Fact]
    public async Task EnsureThumbnail_NoDelegate_IsNoOp()
    {
        var vm = new PageThumbnailViewModel(0);

        await vm.EnsureThumbnailAsync(default);

        vm.Thumbnail.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_DisposesThumbnail()
    {
        var produced = new List<FakePageRender>();
        var vm = new PageThumbnailViewModel(0, Renderer(produced));
        await vm.EnsureThumbnailAsync(default);

        vm.Dispose();

        produced[0].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Strip_WithRenderDelegate_RendersThumbnailsAndDisposesOnDispose()
    {
        var produced = new List<FakePageRender>();
        var strip = new ThumbnailStripViewModel(
            3,
            (_, _, _) => Task.CompletedTask,
            _ => { },
            Renderer(produced));

        await strip.Pages[1].EnsureThumbnailAsync(default);
        strip.Pages[1].Thumbnail.Should().NotBeNull();

        strip.Dispose();
        produced[0].IsDisposed.Should().BeTrue();
    }
}
