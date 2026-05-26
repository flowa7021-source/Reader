using System.Diagnostics.CodeAnalysis;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace Foliant.Plugin.DjVu;

/// <summary>Resolved absolute paths to the DjVuLibre CLI executables.</summary>
public sealed record DjvuToolset(string DdjvuPath, string DjvusedPath)
{
#if WINDOWS
    private const string RegistrySubKey = @"Software\Foliant\Plugins\DjVu";
#endif
    // DjVuLibre binaries are `*.exe` on Windows, bare names elsewhere (the plugin multi-targets
    // and its tests run on Linux); resolve the platform-appropriate name.
    private static string DdjvuExe => OperatingSystem.IsWindows() ? "ddjvu.exe" : "ddjvu";
    private static string DjvusedExe => OperatingSystem.IsWindows() ? "djvused.exe" : "djvused";

    /// <summary>
    /// Resolves <c>ddjvu</c>/<c>djvused</c> from <c>HKCU\Software\Foliant\Plugins\DjVu</c>
    /// (value = install directory), falling back to <c>PATH</c>. Returns <see langword="false"/>
    /// when neither executable can be located.
    /// </summary>
    public static bool TryResolve([NotNullWhen(true)] out DjvuToolset? toolset)
    {
        string? installDir = ReadInstallDir();

        string? ddjvu = ResolveExe(DdjvuExe, installDir);
        string? djvused = ResolveExe(DjvusedExe, installDir);

        if (ddjvu is null || djvused is null)
        {
            toolset = null;
            return false;
        }

        toolset = new DjvuToolset(ddjvu, djvused);
        return true;
    }

    private static string? ReadInstallDir()
    {
#if WINDOWS
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
        return key?.GetValue(null) as string;
#else
        // Non-Windows: no registry; rely on PATH resolution (see ResolveExe).
        return null;
#endif
    }

    private static string? ResolveExe(string exeName, string? installDir)
    {
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            string candidate = Path.Combine(installDir, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath(exeName);
    }

    private static string? FindOnPath(string exeName)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
