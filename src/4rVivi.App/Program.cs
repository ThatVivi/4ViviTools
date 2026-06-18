using System;
using System.IO;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Serilog;
using FourRVivi.App.Services;

namespace FourRVivi.App;

internal static class Program
{
    public static IConfiguration Configuration { get; private set; } = default!;
    public static FourRVivi.Core.Configuration.AppSettings Settings { get; private set; } = new();

    [STAThread]
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(AppLog.LogDir, "App.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        try
        {
            Configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("Config/appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            Settings = Configuration.Get<FourRVivi.Core.Configuration.AppSettings>() ?? new();
            AppLog.Init();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal startup error");
            AppLog.Crash(ex);
        }
        finally { Log.CloseAndFlush(); }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
