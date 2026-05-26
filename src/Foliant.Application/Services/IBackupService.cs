namespace Foliant.Application.Services;

/// <summary>
/// Резервирует пользовательские данные (настройки, лицензия, триал, autosave) перед апгрейдом
/// (PROJECT_BOARD §7.4) в каталог <c>Backup\{label}\</c>. Best-effort: отсутствующие источники
/// пропускаются, не бросает на единичной ошибке копирования.
/// </summary>
public interface IBackupService
{
    /// <summary>Скопировать существующие пользовательские данные в подкаталог <paramref name="label"/>
    /// (обычно <c>v{старая-версия}_{timestamp}</c>). Возвращает путь к созданному бэкапу, либо
    /// <c>null</c>, если копировать было нечего.</summary>
    Task<string?> BackupUserDataAsync(string label, CancellationToken ct);
}
