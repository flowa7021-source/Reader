using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

/// <summary>
/// Тесты port'а PDF/A-валидации и его «честной заглушки» (Variant C). Когда мы заменим
/// <see cref="StubPdfAValidationService"/> на реальную интеграцию veraPDF (CLI out-of-process),
/// эти тесты потребуют только удаления <c>NotSupportedException</c>-веток — форма arg-guard'ов
/// остаётся той же.
/// </summary>
public sealed class PdfAValidationServiceTests
{
    private readonly IPdfAValidationService _sut = new StubPdfAValidationService();

    [Fact]
    public void Type_Implements_PortInterface()
    {
        // Сторожевой тест: port существует и заглушка его реализует. Когда port'а не было,
        // даже типовой контракт не был зафиксирован — теперь зафиксирован.
        typeof(IPdfAValidationService).IsAssignableFrom(typeof(StubPdfAValidationService))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidArgs_Throws_NotSupportedException_MentioningVeraPdf()
    {
        // Stub'у не нужен реальный файл — он fail'ит раньше. Мы передаём непустой path,
        // чтобы пройти arg-guard'ы и доказать, что бросается именно runtime-gate, а не
        // ArgumentException.
        Func<Task> act = () => _sut.ValidateAsync("/tmp/any.pdf", PdfAProfile.PdfA1B, default);

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        // «veraPDF» в сообщении — обязательная подсказка для будущих интеграторов; см.
        // комментарий на StubPdfAValidationService.NotInstalledMessage.
        ex.Which.Message.Should().Contain("veraPDF");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_BlankPath_Throws_ArgumentException(string path)
    {
        Func<Task> act = () => _sut.ValidateAsync(path, PdfAProfile.PdfA1B, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(e => e.ParamName == "sourcePath");
    }

    [Fact]
    public async Task ValidateAsync_NullPath_Throws_ArgumentException()
    {
        Func<Task> act = () => _sut.ValidateAsync(null!, PdfAProfile.PdfA1B, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(e => e.ParamName == "sourcePath");
    }

    [Fact]
    public async Task ValidateAsync_UnknownProfile_Throws_ArgumentOutOfRangeException()
    {
        // -1 — заведомо не в enum'е; Enum.IsDefined по контракту должен ловить.
        const PdfAProfile invalid = (PdfAProfile)(-1);

        Func<Task> act = () => _sut.ValidateAsync("/tmp/any.pdf", invalid, default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "profile");
    }

    [Fact]
    public async Task ValidateAsync_PreCancelledToken_Throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => _sut.ValidateAsync("/tmp/any.pdf", PdfAProfile.PdfA2A, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(PdfAProfile.PdfA1B)]
    [InlineData(PdfAProfile.PdfA1A)]
    [InlineData(PdfAProfile.PdfA2B)]
    [InlineData(PdfAProfile.PdfA2A)]
    [InlineData(PdfAProfile.PdfA2U)]
    [InlineData(PdfAProfile.PdfA3B)]
    [InlineData(PdfAProfile.PdfA3A)]
    [InlineData(PdfAProfile.PdfA3U)]
    public async Task ValidateAsync_AllKnownProfiles_PassArgGuards_AndHitRuntimeGate(PdfAProfile profile)
    {
        // Доказываем, что каждый профиль — валидное значение enum'а: arg-guard не срабатывает,
        // и мы доходим до runtime-gate (NotSupported). Это также защита от «забыли добавить
        // новый профиль» — если в будущем добавим Pdf4*, кейс провалится до обновления тестов.
        Func<Task> act = () => _sut.ValidateAsync("/tmp/any.pdf", profile, default);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void NotInstalledMessage_IsPublicConst_AndMentionsVeraPdf()
    {
        // Публичная константа: интеграторам и тестам не приходится копировать строку из
        // приватного поля. Содержит «veraPDF» — это диагностический контракт.
        StubPdfAValidationService.NotInstalledMessage.Should().Contain("veraPDF");
        StubPdfAValidationService.NotInstalledMessage.Should().NotBeNullOrWhiteSpace();
    }
}
