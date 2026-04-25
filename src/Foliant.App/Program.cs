using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Foliant.App.Composition;
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
        using var host = HostBuilder.Build(args);

        Log.Information("App started (version {Version})", typeof(Program).Assembly.GetName().Version);

        try
        {
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
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
