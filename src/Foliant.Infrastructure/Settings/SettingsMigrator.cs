namespace Foliant.Infrastructure.Settings;

internal static class SettingsMigrator
{
    public static AppSettings Migrate(AppSettings raw)
    {
        // Phase 0: одна версия. При появлении V2 — сюда добавится цепочка миграций
        // (V1 → V2 → V3 → ...). См. IMPLEMENTATION_PLAN.md, раздел 5.8.
        if (raw.Version == AppSettingsV1.SchemaVersion) return raw;

        // Незнакомая будущая версия — возвращаем дефолты, не теряя пользователю файл.
        return AppSettings.Default;
    }
}
