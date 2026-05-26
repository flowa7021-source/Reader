namespace Foliant.Application.Services;

/// <summary>
/// Результат проверки обновлений (PROJECT_BOARD §7.4). <see cref="CurrentVersion"/> —
/// версия запущенной сборки; <see cref="LatestVersion"/> — последний релиз на GitHub
/// (или <c>null</c>, если проверка пропущена/не удалась). <see cref="UpdateAvailable"/>
/// истинно только когда найдена строго более новая версия.
/// </summary>
public sealed record UpdateCheckResult(Version CurrentVersion, Version? LatestVersion, bool UpdateAvailable)
{
    /// <summary>Снимок «проверка не дала повода обновиться» — для throttle/opt-out/сбоя сети.</summary>
    public static UpdateCheckResult NotAvailable(Version current) => new(current, null, false);
}

/// <summary>
/// Проверяет наличие новой версии приложения. Реализация сама применяет throttle
/// (не чаще раза в сутки) и opt-out из настроек; вызывающий код только обрабатывает
/// результат. Никогда не бросает на сетевых сбоях — возвращает <c>UpdateAvailable=false</c>.
/// </summary>
public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct);
}

/// <summary>
/// Узкий порт к источнику релизов (GitHub). Выделен отдельно, чтобы логика сравнения
/// версий и throttle в <c>GitHubUpdateCheckService</c> тестировалась без HTTP — мок
/// этого интерфейса возвращает «сырой» тег вроде <c>v0.2.0</c>. Возвращает <c>null</c>
/// при любой ошибке/отсутствии релизов.
/// </summary>
public interface IReleaseSource
{
    Task<string?> GetLatestTagAsync(CancellationToken ct);
}
