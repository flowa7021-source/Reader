using FluentAssertions;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Application.Tests.UseCases;

public sealed class OpenDocumentUseCaseTests : IDisposable
{
    private readonly string _tmpFile;

    public OpenDocumentUseCaseTests()
    {
        _tmpFile = Path.Combine(Path.GetTempPath(), "foliant-test-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(_tmpFile, "x");
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_tmpFile);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task ExecuteAsync_PicksFirstLoader_ThatCanLoad()
    {
        var loaderA = LoaderThatCannot();
        var doc = Substitute.For<IDocument>();
        var loaderB = LoaderThatLoads(doc);
        var loaderC = LoaderThatCannot();
        var sut = NewSut(loaderA, loaderB, loaderC);

        var result = await sut.ExecuteAsync(_tmpFile, default);

        result.Should().BeSameAs(doc);
        loaderA.Received().CanLoad(_tmpFile);
        loaderB.Received().CanLoad(_tmpFile);
        await loaderB.Received().LoadAsync(_tmpFile, Arg.Any<CancellationToken>());
        loaderC.DidNotReceive().CanLoad(Arg.Any<string>());  // не должен запрашиваться после успеха
    }

    [Fact]
    public async Task ExecuteAsync_NoLoaderCanLoad_Throws()
    {
        var sut = NewSut(LoaderThatCannot(), LoaderThatCannot());

        var act = () => sut.ExecuteAsync(_tmpFile, default);

        var ex = await act.Should().ThrowAsync<UnsupportedDocumentException>();
        ex.Which.Path.Should().Be(_tmpFile);
    }

    [Fact]
    public async Task ExecuteAsync_FileMissing_ThrowsFileNotFound()
    {
        var sut = NewSut(LoaderThatCannot());

        var act = () => sut.ExecuteAsync("/no/such/file.pdf", default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_BadPath_Throws(string? path)
    {
        var sut = NewSut(LoaderThatCannot());

        var act = () => sut.ExecuteAsync(path!, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private OpenDocumentUseCase NewSut(params IDocumentLoader[] loaders) =>
        new(loaders, NullLogger<OpenDocumentUseCase>.Instance);

    private static IDocumentLoader LoaderThatCannot()
    {
        var loader = Substitute.For<IDocumentLoader>();
        loader.CanLoad(Arg.Any<string>()).Returns(false);
        return loader;
    }

    private static IDocumentLoader LoaderThatLoads(IDocument document)
    {
        var loader = Substitute.For<IDocumentLoader>();
        loader.CanLoad(Arg.Any<string>()).Returns(true);
        loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(document);
        return loader;
    }
}
