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

    public static string BookmarksDb => Path.Combine(RoamingAppData, "bookmarks.db");

    private static string EnsureExists(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
