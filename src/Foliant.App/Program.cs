using System.Windows;
using Foliant.App.Composition;
using Foliant.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Foliant.App;

public static class Program
{
    [STAThread]
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
