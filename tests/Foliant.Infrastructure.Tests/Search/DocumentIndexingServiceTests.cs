using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Search;
using Foliant.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Search;

public sealed class DocumentIndexingServiceTests
{
    private const string FakeFingerprint = "abc123";
    private const string FakePath = "/docs/test.pdf";

    private readonly IFtsIndex _fts = Substitute.For<IFtsIndex>();
    private readonly IFileFingerprint _fp = Substitute.For<IFileFingerprint>();
    private readonly DocumentIndexingService _sut;

    public DocumentIndexingServiceTests()
    {
        _fp.ComputeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(FakeFingerprint);

        _fts.IndexDocumentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IAsyncEnumerable<TextLayer>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _sut = new DocumentIndexingService(_fts, _fp, NullLogger<DocumentIndexingService>.Instance);
    }

    [Fact]
    public async Task ProcessRequest_ComputesFingerprintAndIndexes()
    {
        var doc = MakeDoc(["hello world"]);
        var request = new DocumentIndexingService.IndexRequest(doc, FakePath);

        await _sut.ProcessRequestAsync(request, default);

        await _fp.Received(1).ComputeAsync(FakePath, Arg.Any<CancellationToken>());
        await _fts.Received(1).IndexDocumentAsync(
            FakeFingerprint,
            FakePath,
            Arg.Any<IAsyncEnumerable<TextLayer>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRequest_FingerprintThrows_IsLoggedNotPropagated()
    {
        _fp.ComputeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<string>(new IOException("disk error")));

        var doc = MakeDoc(["text"]);
        var request = new DocumentIndexingService.IndexRequest(doc, FakePath);

        var act = () => _sut.ProcessRequestAsync(request, default);

        await act.Should().NotThrowAsync();
        await _fts.DidNotReceive().IndexDocumentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IAsyncEnumerable<TextLayer>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRequest_FtsThrows_IsLoggedNotPropagated()
    {
        _fts.IndexDocumentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IAsyncEnumerable<TextLayer>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("index locked")));

        var doc = MakeDoc(["text"]);
        var request = new DocumentIndexingService.IndexRequest(doc, FakePath);

        var act = () => _sut.ProcessRequestAsync(request, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessRequest_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var doc = MakeDoc(["text"]);
        var request = new DocumentIndexingService.IndexRequest(doc, FakePath);

        var act = () => _sut.ProcessRequestAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Enqueue_TriggersFtsIndexing_ViaBackgroundLoop()
    {
        var tcs = new TaskCompletionSource();
        _fts.IndexDocumentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IAsyncEnumerable<TextLayer>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                tcs.TrySetResult();
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _sut.StartAsync(cts.Token);

        var doc = MakeDoc(["hello"]);
        _sut.Enqueue(doc, FakePath);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await _fts.Received(1).IndexDocumentAsync(
            FakeFingerprint,
            FakePath,
            Arg.Any<IAsyncEnumerable<TextLayer>>(),
            Arg.Any<CancellationToken>());

        await _sut.StopAsync(default);
    }

    private static IDocument MakeDoc(string[] pageTexts)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(pageTexts.Length);
        for (int i = 0; i < pageTexts.Length; i++)
        {
            int idx = i;
            doc.GetTextLayerAsync(idx, Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<TextLayer?>(
                   new TextLayer(idx, [new TextRun(pageTexts[idx], 0, 0, 100, 12)])));
        }
        return doc;
    }
}
