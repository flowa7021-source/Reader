namespace Foliant.Application.Settings;

/// <summary>
/// Snapshot настроек приложения. Schema-versioned: каждая мажорная инкарнация —
/// новый record (AppSettingsV2 и т.д.) + миграция в SettingsMigrator.
/// </summary>
public sealed record AppSettings
{
    public int Version { get; init; } = AppSettingsVersion.Current;

    public string Theme { get; init; } = "Auto";   // Auto | Light | Dark | HighContrast

    public string Language { get; init; } = "ru";  // BCP-47 language tag

    public CacheSettings Cache { get; init; } = new();

    public OcrSettings Ocr { get; init; } = new();

    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>Когда последний раз проверяли обновления (PROJECT_BOARD §7.4). <c>null</c> —
    /// ещё ни разу; используется для throttle «не чаще раза в сутки».</summary>
    public DateTimeOffset? LastUpdateCheckTime { get; init; }

    /// <summary>Opt-out проверки обновлений. По умолчанию включено.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Opt-in записи crash-отчётов (§3.1: только crash-reports, по согласию).
    /// По умолчанию выключено — пользователь включает явно.</summary>
    public bool CrashReportingEnabled { get; init; }

    /// <summary>Версия приложения на прошлом запуске. При изменении на старте делается
    /// auto-backup пользовательских данных (§7.4). <c>null</c> — первый запуск (бэкап не нужен).</summary>
    public string? LastRunVersion { get; init; }

    public static AppSettings Default { get; } = new();
}

public sealed record CacheSettings
{
    public long DiskLimitBytes { get; init; } = 5L * 1024 * 1024 * 1024;  // 5 ГБ
    public int PerDocumentCapPercent { get; init; } = 30;
    public bool ClearOnExit { get; init; }
    public bool DpapiEncryptForProtectedDocs { get; init; }
}

public sealed record OcrSettings
{
    public string DefaultLanguage { get; init; } = "rus+eng";
    public int MaxParallelPages { get; init; } = 4;
    public bool AutoOcrOpenedScans { get; init; }
    public OcrModelTier ModelTier { get; init; } = OcrModelTier.Basic;
}

// Уровень OCR-моделей; соответствует -Tier в tools/fetch-natives.ps1.
public enum OcrModelTier
{
    Basic,
    Standard,
    Full,
}

internal static class AppSettingsVersion
{
    public const int Current = 2;
}
