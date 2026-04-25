using Foliant.Application.UseCases;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Foliant.Infrastructure.Caching;
using Foliant.Infrastructure.Settings;
using Foliant.Infrastructure.Storage;
using Foliant.UI;
using Foliant.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foliant.App.Composition;

internal static class HostBuilder
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        ConfigureLogging(builder);
        ConfigureServices(builder.Services);

        return builder.Build();
    }

    private static void ConfigureLogging(HostApplicationBuilder builder)
    {
        var logFile = Path.Combine(AppPaths.Logs, "foliant-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 50 * 1024 * 1024)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IFileFingerprint, FileFingerprint>();
        services.AddSingleton<ISettingsStore>(sp =>
            new JsonSettingsStore(AppPaths.SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        // Cache (RAM + Disk). Жёсткий потолок RAM: min(15 % системной, 1 ГБ); по плану.
        var ramLimit = Math.Min(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 100 * 15, 1L * 1024 * 1024 * 1024);
        services.AddSingleton(new MemoryPageCache(capacityBytes: Math.Max(ramLimit, 128L * 1024 * 1024)));
        services.AddSingleton<IDiskCache>(sp =>
            new SqliteDiskCache(AppPaths.Cache, sp.GetRequiredService<ILogger<SqliteDiskCache>>()));

        // Document engines (loaders регистрируются как IDocumentLoader; OpenDocumentUseCase
        // получает IEnumerable<IDocumentLoader> и выбирает по факту CanLoad).
        services.AddSingleton<IDocumentLoader, PdfDocumentLoader>();

        // Application
        services.AddSingleton<OpenDocumentUseCase>();

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }
}
