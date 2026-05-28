namespace Foliant.Infrastructure.Storage;

/// <summary>
/// Стандартные пути приложения. Все каталоги — гарантированно существуют после первого Get*.
/// </summary>
public static class AppPaths
{
    public const string AppName = "Foliant";

    public static string LocalAppData => EnsureExists(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName));

    public static string RoamingAppData => EnsureExists(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName));

    public static string Logs => EnsureExists(Path.Combine(LocalAppData, "Logs"));

    public static string Cache => EnsureExists(Path.Combine(LocalAppData, "Cache"));

    public static string Autosave => EnsureExists(Path.Combine(LocalAppData, "Autosave"));

    public static string Backup => EnsureExists(Path.Combine(LocalAppData, "Backup"));

    public static string CrashReports => EnsureExists(Path.Combine(LocalAppData, "CrashReports"));

    public static string Annotations => EnsureExists(Path.Combine(LocalAppData, "Annotations"));

    public static string Bookmarks => EnsureExists(Path.Combine(LocalAppData, "Bookmarks"));

    public static string SettingsFile => Path.Combine(RoamingAppData, "settings.json");

    public static string LicenseFile => Path.Combine(RoamingAppData, "license.key");

    public static string TrialFile => Path.Combine(RoamingAppData, "trial.dat");

    public static string TrialMarkerFile => Path.Combine(Autosave, ".trial-marker");

    public static string BookmarksDb => Path.Combine(RoamingAppData, "bookmarks.db");

    /// <summary>Каталог engine-плагинов рядом с исполняемым (<c>&lt;appdir&gt;/plugins</c>).
    /// Сборки плагинов кладутся сюда post-build; <c>PluginCatalog</c> сканирует папку в рантайме.
    /// Не в LocalAppData — плагины поставляются с приложением, а не с пользовательскими данными.</summary>
    public static string Plugins => EnsureExists(Path.Combine(AppContext.BaseDirectory, "plugins"));

    /// <summary>JSON-файл персистентной истории поиска (most-recent-first, cap 50). Лежит в
    /// Roaming, чтобы переезжать вместе с пользователем между машинами в доменной среде —
    /// как и settings.json.</summary>
    public static string SearchHistoryFile => Path.Combine(RoamingAppData, "search-history.json");

    private static string EnsureExists(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
