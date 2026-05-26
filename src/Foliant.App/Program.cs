using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using Foliant.App.Composition;
using Foliant.Application.Services;
using Foliant.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Foliant.App;

internal static class Program
{
    [STAThread]
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Top-level handler must log and translate any unhandled exception into a non-zero exit code.")]
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var host = AppHostBuilder.Build(args);

        Log.Information("App started (version {Version})", typeof(Program).Assembly.GetName().Version);

        try
        {
            // Pre-load settings + apply culture before any window is created — иначе на первом
            // рендере XAML будет видна вспышка default-локали (en) до того, как InitializeAsync
            // отработает в Loaded-обработчике.
            var settings = host.Services.GetRequiredService<ISettingsService>();
            settings.LoadAsync(default).GetAwaiter().GetResult();
            var localization = host.Services.GetRequiredService<ILocalizationService>();
            localization.SetCulture(settings.Current.Language);

            // Auto-backup пользовательских данных при первом запуске после апгрейда (§7.4).
            var currentVersion = typeof(Program).Assembly.GetName().Version?.ToString();
            if (settings.Current.LastRunVersion is { } previousVersion
                && !string.Equals(previousVersion, currentVersion, StringComparison.Ordinal))
            {
                var label = "v" + previousVersion + "_"
                    + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                host.Services.GetService<IBackupService>()?.BackupUserDataAsync(label, default).GetAwaiter().GetResult();
            }

            if (!string.Equals(settings.Current.LastRunVersion, currentVersion, StringComparison.Ordinal))
            {
                settings.UpdateAsync(s => s with { LastRunVersion = currentVersion }, default).GetAwaiter().GetResult();
            }

            // Стартуем hosted-сервисы (CacheJanitor, DocumentIndexingService) — без этого
            // фоновая индексация FTS5 и эвикция кэша не работают.
            host.Start();

            if (args.Contains("--smoke"))
            {
                Log.Information("Smoke run requested — exit immediately after bootstrap.");
                return 0;
            }

            var app = new System.Windows.Application();
            var window = host.Services.GetRequiredService<MainWindow>();
            return app.Run(window);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception in main thread");
            host.Services.GetService<ICrashReporter>()?.Report(ex, "main-thread");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
