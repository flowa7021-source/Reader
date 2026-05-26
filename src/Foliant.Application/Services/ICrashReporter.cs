namespace Foliant.Application.Services;

/// <summary>
/// Записывает отчёт о необработанном исключении в локальный каталог, ЕСЛИ пользователь
/// включил opt-in (§3.1: только crash-reports, по согласию). Реализация — best-effort и
/// не бросает: вызывается из обработчиков сбоев, когда приложение уже падает.
/// </summary>
public interface ICrashReporter
{
    /// <summary>Записать crash-отчёт. Возвращает путь к файлу отчёта, либо <c>null</c>,
    /// если репортинг выключен или запись не удалась.</summary>
    /// <param name="exception">Необработанное исключение.</param>
    /// <param name="context">Где произошёл сбой (напр. "main-thread", "dispatcher").</param>
    string? Report(Exception exception, string context);
}
