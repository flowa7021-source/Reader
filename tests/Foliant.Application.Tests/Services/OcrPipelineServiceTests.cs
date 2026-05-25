using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class OcrPipelineServiceTests
{
    private const string Fp = "pipe-fp";

    private readonly IOcrEngine _engine = Substitute.For<IOcrEngine>();
    private readonly IOcrCache _cache = Substitute.For<IOcrCache>();
    private readonly OcrPipelineService _sut;

    public OcrPipelineServiceTests()
    {
        _engine.Version.Returns(1);
        // Default: cache miss → engine returns empty layer for any page.
        _cache.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>())
              .Returns((TextLayer?)null);
        _engine.RecognizeAsync(Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>())
               .Returns(ci => TextLayer.Empty(ci.ArgAt<int>(1)));
        _cache.PutAsync(Arg.Any<CacheKey>(), Arg.Any<TextLayer>(), Arg.Any<CancellationToken>())
              .Returns(Task.CompletedTask);

        var pageUseCase = new OcrPageUseCase(_engine, _cache, NullLogger<OcrPageUseCase>.Instance);
        _sut = new OcrPipelineService(pageUseCase, NullLogger<OcrPipelineService>.Instance);
    }

    private static IDocument MakeDocument(int pageCount)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(pageCount);
        doc.RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
           .Returns(ci => Task.FromResult(Substitute.For<IPageRender>()));
        return doc;
    }

    // ───── S8/B ─────

    [Fact]
    public async Task RecognizeDocument_ReturnsOneLayerPerPage()
    {
        var doc = MakeDocument(3);

        var result = await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task RecognizeDocument_PageIndexMatchesPosition()
    {
        var doc = MakeDocument(4);

        var result = await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        for (int i = 0; i < result.Count; i++)
        {
            result[i].PageIndex.Should().Be(i);
        }
    }

    [Fact]
    public async Task RecognizeDocument_ReportsProgressForEachPage()
    {
        int pageCount = 5;
        var doc = MakeDocument(pageCount);
        var reports = new List<OcrProgress>();
        var progress = new Progress<OcrProgress>(p => reports.Add(p));

        await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), progress, default);

        // Give the synchronous progress callbacks a chance to fire (Progress<T> posts to the
        // captured SynchronizationContext; in xUnit that is the thread-pool, so we yield).
        await Task.Yield();

        reports.Should().HaveCount(pageCount);
        reports[^1].CompletedPages.Should().Be(pageCount);
        reports[^1].TotalPages.Should().Be(pageCount);
    }

    [Fact]
    public async Task RecognizeDocument_EmptyDocument_ReturnsEmpty()
    {
        var doc = MakeDocument(0);

        var result = await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RecognizeDocument_RenderFailure_SubstitutesEmptyAndContinues()
    {
        // Page 1 render throws; pages 0 and 2 succeed.
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(3);
        doc.RenderPageAsync(0, Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(Substitute.For<IPageRender>()));
        doc.RenderPageAsync(1, Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
           .Throws(new InvalidOperationException("render boom"));
        doc.RenderPageAsync(2, Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(Substitute.For<IPageRender>()));

        var result = await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        result.Should().HaveCount(3);
        result[1].Runs.Should().BeEmpty();   // empty substituted for page 1
        result[0].Runs.Should().NotBeNull(); // page 0 processed normally
    }

    [Fact]
    public async Task RecognizeDocument_EngineFailure_SubstitutesEmptyAndContinues()
    {
        var doc = MakeDocument(3);
        // Engine throws on page 1 only.
        _engine.RecognizeAsync(Arg.Any<IPageRender>(), 1, Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>())
               .Throws(new Exception("ocr boom"));

        var result = await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        result.Should().HaveCount(3);
        result[1].Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task RecognizeDocument_CancellationMidway_Throws()
    {
        using var cts = new CancellationTokenSource();
        int renderCallCount = 0;
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(10);
        doc.RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
           .Returns(ci =>
           {
               renderCallCount++;
               if (renderCallCount == 3)
               {
                   cts.Cancel();
               }
               ci.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();
               return Task.FromResult(Substitute.For<IPageRender>());
           });

        var act = () => _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecognizeDocument_NullArgs_Throw()
    {
        var doc = MakeDocument(1);

        var act1 = () => _sut.RecognizeDocumentAsync(null!, Fp, new OcrOptions(), null, default);
        var act2 = () => _sut.RecognizeDocumentAsync(doc, null!, new OcrOptions(), null, default);
        var act3 = () => _sut.RecognizeDocumentAsync(doc, Fp, null!, null, default);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
        await act3.Should().ThrowAsync<ArgumentNullException>();
    }

    // ───── S8/C: ITextLayerCache wiring ─────

    [Fact]
    public async Task RecognizeDocument_TextCacheHit_SkipsRenderAndEngine()
    {
        var expectedLayer = new TextLayer(0, [new TextRun("cached", 0, 0, 10, 10)]);
        var textCache = Substitute.For<ITextLayerCache>();
        textCache.TryGet(0, out Arg.Any<TextLayer>())
                 .Returns(ci => { ci[1] = expectedLayer; return true; });

        var pageUseCase = new OcrPageUseCase(_engine, _cache, NullLogger<OcrPageUseCase>.Instance);
        var sut = new OcrPipelineService(pageUseCase, NullLogger<OcrPipelineService>.Instance, textCache);
        var doc = MakeDocument(1);

        var result = await sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        result.Should().HaveCount(1);
        result[0].Should().Be(expectedLayer);
        // Render and engine must NOT have been called because the in-memory cache answered.
        await doc.DidNotReceive().RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>());
        await _engine.DidNotReceive().RecognizeAsync(Arg.Any<IPageRender>(), Arg.Any<int>(), Arg.Any<OcrOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecognizeDocument_TextCacheMiss_StoresResultInCache()
    {
        var textCache = Substitute.For<ITextLayerCache>();
        textCache.TryGet(Arg.Any<int>(), out Arg.Any<TextLayer>()).Returns(false);

        var pageUseCase = new OcrPageUseCase(_engine, _cache, NullLogger<OcrPageUseCase>.Instance);
        var sut = new OcrPipelineService(pageUseCase, NullLogger<OcrPipelineService>.Instance, textCache);
        var doc = MakeDocument(2);

        await sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        textCache.Received(1).Put(0, Arg.Any<TextLayer>());
        textCache.Received(1).Put(1, Arg.Any<TextLayer>());
    }

    [Fact]
    public async Task RecognizeDocument_NoTextCache_WorksWithoutIt()
    {
        // Baseline: passing null textCache (the default) must not throw and processes all pages.
        var doc = MakeDocument(3);

        var result = await _sut.RecognizeDocumentAsync(doc, Fp, new OcrOptions(), null, default);

        result.Should().HaveCount(3);
    }
}
