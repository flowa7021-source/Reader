using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// <see cref="StubPdfEncryptionService"/> — заглушка для Q-F30/F31: argument-валидация ДО
/// NotSupported, чтобы caller получил конкретную ошибку на «грязные» вызовы, и явный
/// <see cref="System.NotSupportedException"/> на корректные. Эти тесты фиксируют контракт,
/// чтобы при подключении реальной реализации Phase 3 не пришлось переучивать callers.
/// </summary>
public sealed class StubPdfEncryptionServiceTests
{
    private static readonly PdfEncryptionSpec ValidSpec =
        PdfEncryptionSpec.Create("user", "owner", PdfPermissions.Print);

    [Fact]
    public async Task EncryptAsync_ValidArguments_ThrowsNotSupported()
    {
        IPdfEncryptionService svc = new StubPdfEncryptionService();

        var act = () => svc.EncryptAsync("in.pdf", "out.pdf", ValidSpec, default);

        var ex = await act.Should().ThrowAsync<System.NotSupportedException>();
        ex.Which.Message.Should().Contain("Q-F30/F31");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EncryptAsync_BlankSource_ThrowsArgument(string? source)
    {
        var act = () => new StubPdfEncryptionService().EncryptAsync(source!, "out.pdf", ValidSpec, default);

        await act.Should().ThrowAsync<System.ArgumentException>()
            .Where(e => e.ParamName == "sourcePath");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EncryptAsync_BlankTarget_ThrowsArgument(string? target)
    {
        var act = () => new StubPdfEncryptionService().EncryptAsync("in.pdf", target!, ValidSpec, default);

        await act.Should().ThrowAsync<System.ArgumentException>()
            .Where(e => e.ParamName == "targetPath");
    }

    [Fact]
    public async Task EncryptAsync_NullSpec_ThrowsArgumentNull()
    {
        var act = () => new StubPdfEncryptionService().EncryptAsync("in.pdf", "out.pdf", null!, default);

        await act.Should().ThrowAsync<System.ArgumentNullException>()
            .Where(e => e.ParamName == "spec");
    }

    [Fact]
    public async Task EncryptAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var act = () => new StubPdfEncryptionService().EncryptAsync("in.pdf", "out.pdf", ValidSpec, cts.Token);

        // OperationCanceledException — родитель TaskCanceledException; принимаем оба.
        await act.Should().ThrowAsync<System.OperationCanceledException>();
    }

    [Fact]
    public void Service_Implements_IPdfEncryptionService()
    {
        // Закрепляем, что DI-регистрация увидит сервис через port-интерфейс (а не только
        // через конкретный класс) — это контракт реализации.
        new StubPdfEncryptionService().Should().BeAssignableTo<IPdfEncryptionService>();
    }
}
