using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// «Честная заглушка» (honest stub) для <see cref="IPdfAValidationService"/>: проверяет
/// аргументы (path, profile, отсутствие cancellation) и бросает <see cref="NotSupportedException"/>
/// с понятным сообщением, явно указывая, что нужен veraPDF runtime. Цель — дать остальному
/// приложению типизированный port и DI-binding до того, как мы интегрируем veraPDF через
/// CLI out-of-process (deferred, требует пакетирования JRE + jar и отдельного PR).
///
/// <para>Почему не managed wrapper: единственный существующий .NET-биндинг
/// (<c>Codeuctivity.PdfAValidator</c>) лицензирован под AGPL-3.0, что несовместимо с MIT
/// Foliant — он бы вирусно перевёл всё приложение в AGPL. Сам veraPDF лицензирован под
/// MPL-2.0 / GPL-3.0+ и работает на Java; интеграция через CLI out-of-process (по образцу
/// <c>plugins/Foliant.Plugin.DjVu</c>) совместима по лицензии (process boundary), но
/// откладывается до отдельного PR с пакетированием runtime.</para>
///
/// <para>Стуб всё равно валидирует аргументы — это контракт port'а, и тесты на нём
/// сохранят форму поведения, когда мы заменим заглушку на реальную реализацию.</para>
/// </summary>
public sealed class StubPdfAValidationService : IPdfAValidationService
{
    /// <summary>
    /// Сообщение, доступное и тестам, и интеграторам — содержит подстроку <c>"veraPDF"</c>,
    /// чтобы будущий разработчик при clean-room debug'е сразу понимал, какой runtime ставить
    /// и куда копать (см. <c>https://verapdf.org</c>).
    /// </summary>
    public const string NotInstalledMessage =
        "PDF/A validation is not available: veraPDF runtime is not installed. " +
        "Install veraPDF (https://verapdf.org) and wire a real IPdfAValidationService implementation.";

    public Task<PdfAComplianceResult> ValidateAsync(
        string sourcePath,
        PdfAProfile profile,
        CancellationToken ct)
    {
        // Guard order: cheap arg checks first, then cancellation, then the runtime gate.
        // Even as a stub we honour the port's documented error contract so caller tests
        // written against IPdfAValidationService keep working when we swap to veraPDF.
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path must be a non-empty string.", nameof(sourcePath));
        }

        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown PDF/A profile.");
        }

        ct.ThrowIfCancellationRequested();

        throw new NotSupportedException(NotInstalledMessage);
    }
}
