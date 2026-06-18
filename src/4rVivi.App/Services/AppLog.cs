using System;
using System.IO;

namespace FourRVivi.App.Services;

/// <summary>App-wide logging + crash capture. Writes under %AppData%/4rVivi/Logs.</summary>
public static class AppLog
{
    public static string RootDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi");
    public static string LogDir { get; } = Path.Combine(RootDir, "Logs");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Crash("Unhandled exception", e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Crash("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }
        catch { }
    }

    public static void Crash(Exception? ex) => Crash("Crash", ex);

    public static void Crash(string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(Path.Combine(LogDir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
        try { Serilog.Log.Error(ex, "{Context}", context); } catch { }
    }
}
