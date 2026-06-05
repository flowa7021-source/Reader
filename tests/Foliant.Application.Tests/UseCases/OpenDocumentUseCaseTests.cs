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

    [Fact]
    public async Task ExecuteAsync_PasswordAwareLoader_ReceivesPasswordArgument()
    {
        var doc = Substitute.For<IDocument>();
        var loader = PasswordAwareLoaderThatLoads(doc);
        var sut = NewSut(loader);

        var result = await sut.ExecuteAsync(_tmpFile, "s3cret", default);

        result.Should().BeSameAs(doc);
        // Пароль действительно проброшен в password-aware loader.
        await ((IPasswordAwareDocumentLoader)loader).Received(1)
            .LoadAsync(_tmpFile, "s3cret", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PlainLoader_IgnoresPassword_StillLoads()
    {
        // Обычный IDocumentLoader (не password-aware) вызывается через старый контракт
        // даже если пароль передан — он его просто не видит.
        var doc = Substitute.For<IDocument>();
        var loader = LoaderThatLoads(doc);
        var sut = NewSut(loader);

        var result = await sut.ExecuteAsync(_tmpFile, "ignored", default);

        result.Should().BeSameAs(doc);
        await loader.Received(1).LoadAsync(_tmpFile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PasswordRequired_PropagatesException()
    {
        // Use-case НЕ ловит DocumentPasswordRequiredException — пробрасывает наверх (VM спросит пароль).
        var loader = Substitute.For<IDocumentLoader, IPasswordAwareDocumentLoader>();
        loader.CanLoad(Arg.Any<string>()).Returns(true);
        ((IPasswordAwareDocumentLoader)loader)
            .LoadAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IDocument>>(_ => throw DocumentPasswordRequiredException.ForPath(_tmpFile));
        var sut = NewSut(loader);

        var act = () => sut.ExecuteAsync(_tmpFile, null, default);

        var ex = await act.Should().ThrowAsync<DocumentPasswordRequiredException>();
        ex.Which.Path.Should().Be(_tmpFile);
    }

    private OpenDocumentUseCase NewSut(params IDocumentLoader[] loaders) =>
        new(loaders, NullLogger<OpenDocumentUseCase>.Instance);

    private static IDocumentLoader PasswordAwareLoaderThatLoads(IDocument document)
    {
        // NSubstitute умеет создавать мульти-интерфейсный мок, реализующий оба контракта.
        var loader = Substitute.For<IDocumentLoader, IPasswordAwareDocumentLoader>();
        loader.CanLoad(Arg.Any<string>()).Returns(true);
        ((IPasswordAwareDocumentLoader)loader)
            .LoadAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(document);
        return loader;
    }

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
