using Foliant.Application.Settings;

namespace Foliant.Infrastructure.Settings;

internal static class SettingsMigrator
{
    public static AppSettings Migrate(AppSettings raw)
    {
        AppSettings current = raw;

        // V1 → V2: добавлен OcrSettings.ModelTier; в старых файлах поля нет —
        // дефолтим Basic (как в fetch-natives.ps1 без -Tier).
        if (current.Version == 1)
        {
            current = current with
            {
                Version = 2,
                Ocr = current.Ocr with { ModelTier = OcrModelTier.Basic },
            };
        }

        if (current.Version == 2)
        {
            return current;
        }

        // Незнакомая будущая версия — возвращаем дефолты, не теряя пользователю файл.
        return AppSettings.Default;
    }
}
