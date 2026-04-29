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

    public static AppSettings Default { get; } = new();
}

public sealed record CacheSettings
{
    public long DiskLimitBytes { get; init; } = 5L * 1024 * 1024 * 1024;  // 5 ГБ
    public int  PerDocumentCapPercent { get; init; } = 30;
    public bool ClearOnExit { get; init; }
    public bool DpapiEncryptForProtectedDocs { get; init; }
}

public sealed record OcrSettings
{
    public string DefaultLanguage { get; init; } = "rus+eng";
    public int    MaxParallelPages { get; init; } = 4;
    public bool   AutoOcrOpenedScans { get; init; }
}

internal static class AppSettingsVersion
{
    public const int Current = 1;
}
