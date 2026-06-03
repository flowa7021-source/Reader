using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Набор PDF/A-профилей (ISO 19005), которые умеет проверять валидатор. Имена соответствуют
/// нотации veraPDF (<c>1a</c>, <c>1b</c>, ...). Buckets:
/// <list type="bullet">
/// <item>Part 1 (ISO 19005-1, на базе PDF 1.4): <see cref="PdfA1B"/>, <see cref="PdfA1A"/>.</item>
/// <item>Part 2 (ISO 19005-2, на базе PDF 1.7): <see cref="PdfA2B"/>, <see cref="PdfA2A"/>, <see cref="PdfA2U"/>.</item>
/// <item>Part 3 (ISO 19005-3, разрешает embedded files): <see cref="PdfA3B"/>, <see cref="PdfA3A"/>, <see cref="PdfA3U"/>.</item>
/// </list>
/// Levels: <c>B</c> (Basic, визуальное соответствие), <c>U</c> (Unicode, текст в Unicode), <c>A</c>
/// (Accessible, semantic tags + Unicode). Часть 4 (PDF 2.0) пока вне scope — добавим, когда
/// нижележащий валидатор её поддержит.
/// </summary>
public enum PdfAProfile
{
    PdfA1B,
    PdfA1A,
    PdfA2B,
    PdfA2A,
    PdfA2U,
    PdfA3B,
    PdfA3A,
    PdfA3U,
}

/// <summary>
/// Порт PDF/A-валидации: проверяет, удовлетворяет ли PDF выбранному профилю (ISO 19005), и
/// возвращает <see cref="PdfAComplianceResult"/> с булевым итогом + полным списком нарушений
/// (правило + страница). Целевая аудитория — юристы и архивы: им нужно подтвердить
/// соответствие архивному стандарту до подписи / долгосрочного хранения.
///
/// <para>Scope MVP: только валидация. Создание PDF/A (преобразование обычного PDF → PDF/A)
/// — отдельная задача и сюда не входит.</para>
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустой / whitespace <c>sourcePath</c> → <see cref="System.ArgumentException"/>.</item>
/// <item>Файл не существует → <see cref="System.IO.FileNotFoundException"/>.</item>
/// <item>Неизвестный <c>profile</c> → <see cref="System.ArgumentOutOfRangeException"/>.</item>
/// <item>Нижележащий движок недоступен (нет runtime, неверная установка) →
/// <see cref="System.NotSupportedException"/> с сообщением, указывающим, какой runtime нужен
/// (см. <c>StubPdfAValidationService</c> — honest stub до интеграции veraPDF).</item>
/// <item>Битый PDF / IO-сбой / сбой валидатора → пробрасывается caller'у; реализациям
/// рекомендуется оборачивать чужие исключения в <see cref="System.InvalidOperationException"/>
/// с понятным сообщением.</item>
/// </list>
/// </summary>
public interface IPdfAValidationService
{
    Task<PdfAComplianceResult> ValidateAsync(string sourcePath, PdfAProfile profile, CancellationToken ct);
}
