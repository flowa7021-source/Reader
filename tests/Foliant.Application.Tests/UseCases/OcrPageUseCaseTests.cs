using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Application.Tests.UseCases;

public sealed class OcrPageUseCaseTests
{
    private const string Fingerprint = "fp-test";
    private const int PageIndex = 3;
    private const int EngineVersion = 42;

    private readonly IOcrEngine _engine = Substitute.For<IOcrEngine>();
    private readonly IOcrCache _cache = Substitute.For<IOcrCache>();
    private readonly IPageRender _render = Substitute.For<IPageRender>();
    private readonly OcrPageUseCase _sut;

    public OcrPageUseCaseTests()
    {
        _engine.Version.Returns(EngineVersion);
        _sut = new OcrPageUseCase(_engine, _cache, NullLogger<OcrPageUseCase>.Instance);
    }

    [Fact]
    public async Task Execute_CacheHit_SkipsEngine()
    {
        var cached = new TextLayer(PageIndex, [new TextRun("cached", 0, 0, 100, 12)]);
        _cache.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns(cached);

        var result = await _sut.ExecuteAsync(_render, Fingerprint, PageIndex, new OcrOptions(), default);

        result.Should().BeSameAs(cached);
        await _engine.DidNotReceive().RecognizeAsync(
            Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().PutAsync(Arg.Any<CacheKey>(), Arg.Any<TextLayer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_CacheMiss_RunsEngineAndStoresResult()
    {
        _cache.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns((TextLayer?)null);
        var produced = new TextLayer(PageIndex, [new TextRun("ocr", 0, 0, 100, 12)]);
        _engine.RecognizeAsync(Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>())
               .Returns(produced);

        var result = await _sut.ExecuteAsync(_render, Fingerprint, PageIndex, new OcrOptions(), default);

        result.Should().BeSameAs(produced);
        await _cache.Received(1).PutAsync(
            Arg.Is<CacheKey>(k =>
                k.DocFingerprint == Fingerprint &&
                k.PageIndex == PageIndex &&
                k.EngineVersion == EngineVersion &&
                k.ZoomBucket == 0 &&
                k.Flags == OcrPageUseCase.OcrFlag),
            produced,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_UsesEngineVersion_InCacheKey()
    {
        _engine.Version.Returns(7);
        _cache.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns((TextLayer?)null);
        _engine.RecognizeAsync(Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>())
               .Returns(new TextLayer(PageIndex, []));

        await _sut.ExecuteAsync(_render, Fingerprint, PageIndex, new OcrOptions(), default);

        await _cache.Received(1).TryGetAsync(
            Arg.Is<CacheKey>(k => k.EngineVersion == 7),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_OcrFlag_DistinguishesFromRenderKeys()
    {
        _cache.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns((TextLayer?)null);
        _engine.RecognizeAsync(Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>())
               .Returns(new TextLayer(PageIndex, []));

        await _sut.ExecuteAsync(_render, Fingerprint, PageIndex, new OcrOptions(), default);

        await _cache.Received(1).TryGetAsync(
            Arg.Is<CacheKey>(k => (k.Flags & OcrPageUseCase.OcrFlag) != 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_PassesOptions_ToEngine()
    {
        var options = new OcrOptions("rus");
        _cache.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns((TextLayer?)null);
        _engine.RecognizeAsync(Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>())
               .Returns(new TextLayer(PageIndex, []));

        await _sut.ExecuteAsync(_render, Fingerprint, PageIndex, options, default);

        await _engine.Received(1).RecognizeAsync(
            _render, PageIndex, options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NullArgs_Throw()
    {
        var act1 = () => _sut.ExecuteAsync(null!, Fingerprint, PageIndex, new OcrOptions(), default);
        var act2 = () => _sut.ExecuteAsync(_render, null!, PageIndex, new OcrOptions(), default);
        var act3 = () => _sut.ExecuteAsync(_render, Fingerprint, PageIndex, null!, default);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
        await act3.Should().ThrowAsync<ArgumentNullException>();
    }
}
