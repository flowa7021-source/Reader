using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class ThumbnailRendererTests
{
    [Fact]
    public async Task GetThumbnailAsync_ReturnsRenderedDimsAndPixels()
    {
        var render = new PixelPageRender(width: 4, height: 3);
        var document = StubDocument(render);
        var cache = new ThumbnailCache();
        var sut = new ThumbnailRenderer();

        var thumb = await sut.GetThumbnailAsync(document, cache, pageIndex: 0, ct: default);

        thumb.WidthPx.Should().Be(4);
        thumb.HeightPx.Should().Be(3);
        thumb.Stride.Should().Be(16);
        thumb.Bgra32.ToArray().Should().Equal(render.Bgra32.ToArray());
    }

    [Fact]
    public async Task GetThumbnailAsync_RendersWithSmallSize_AnnotationsOff()
    {
        RenderOptions? captured = null;
        var document = Substitute.For<IDocument>();
        document
            .RenderPageAsync(Arg.Any<int>(), Arg.Do<RenderOptions>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IPageRender>(new PixelPageRender(2, 2)));
        var sut = new ThumbnailRenderer(maxWidthPx: 128);

        await sut.GetThumbnailAsync(document, new ThumbnailCache(), pageIndex: 0, ct: default);

        captured.Should().NotBeNull();
        captured!.MaxWidthPx.Should().Be(128);
        captured.RenderAnnotations.Should().BeFalse();
    }

    [Fact]
    public async Task GetThumbnailAsync_SecondCall_HitsCache_RendersOnce()
    {
        var document = StubDocument(new PixelPageRender(4, 3));
        var cache = new ThumbnailCache();
        var sut = new ThumbnailRenderer();

        var first = await sut.GetThumbnailAsync(document, cache, pageIndex: 2, ct: default);
        var second = await sut.GetThumbnailAsync(document, cache, pageIndex: 2, ct: default);

        await document.Received(1).RenderPageAsync(2, Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>());
        second.WidthPx.Should().Be(first.WidthPx);
        second.HeightPx.Should().Be(first.HeightPx);
        second.Stride.Should().Be(first.Stride);
        second.Bgra32.ToArray().Should().Equal(first.Bgra32.ToArray());
    }

    [Fact]
    public async Task GetThumbnailAsync_CancellationBeforeRender_Propagates_NoRender()
    {
        var document = StubDocument(new PixelPageRender(2, 2));
        var sut = new ThumbnailRenderer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetThumbnailAsync(document, new ThumbnailCache(), pageIndex: 0, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await document.DidNotReceive().RenderPageAsync(
            Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetThumbnailAsync_CancellationDuringRender_Propagates()
    {
        var document = Substitute.For<IDocument>();
        document
            .RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<IPageRender>>(_ => throw new OperationCanceledException());
        var sut = new ThumbnailRenderer();

        var act = () => sut.GetThumbnailAsync(document, new ThumbnailCache(), pageIndex: 0, ct: default);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetThumbnailAsync_NullDocument_Throws()
    {
        var sut = new ThumbnailRenderer();
        var act = () => sut.GetThumbnailAsync(null!, new ThumbnailCache(), pageIndex: 0, ct: default);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetThumbnailAsync_NegativePageIndex_Throws()
    {
        var sut = new ThumbnailRenderer();
        var act = () => sut.GetThumbnailAsync(StubDocument(new PixelPageRender(2, 2)), new ThumbnailCache(), -1, default);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static IDocument StubDocument(IPageRender render)
    {
        var document = Substitute.For<IDocument>();
        document
            .RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(render));
        return document;
    }

    private sealed class PixelPageRender : IPageRender
    {
        private readonly byte[] _data;

        public PixelPageRender(int width, int height)
        {
            WidthPx = width;
            HeightPx = height;
            Stride = width * 4;
            _data = new byte[Stride * height];
            for (var i = 0; i < _data.Length; i++)
            {
                _data[i] = (byte)(i % 251);
            }
        }

        public int WidthPx { get; }
        public int HeightPx { get; }
        public int Stride { get; }
        public ReadOnlyMemory<byte> Bgra32 => _data;

        public void Dispose()
        {
        }
    }
}
