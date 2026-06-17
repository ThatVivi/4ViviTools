using System.IO;

namespace FourRVivi.App.Services;

/// <summary>Zero-dependency logging + crash capture. Writes to %AppData%/4rVivi/Logs and
/// creates the app folders (Logs/Plugins/Profiles/Config) on startup.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi");
    public static string LogDir { get; } = Path.Combine(Root, "Logs");

    public static void Init()
    {
        try
        {
            foreach (var d in new[] { "Logs", "Plugins", "Profiles", "Config" })
                Directory.CreateDirectory(Path.Combine(Root, d));

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Crash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            { Crash("UnobservedTaskException", e.Exception); e.SetObserved(); };

            Info($"4rVivi started (v{typeof(AppLog).Assembly.GetName().Version}).");
        }
        catch { /* logging must never throw */ }
    }

    public static void Info(string message) => Write("app.log", "INFO ", message, null);
    public static void Error(string message, Exception? ex = null) => Write("error.log", "ERROR", message, ex);
    public static void Crash(string message, Exception? ex = null)
    {
        Write("error.log", "CRASH", message, ex);
        Write("crash.log", "CRASH", message, ex);
    }

    private static void Write(string file, string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(Path.Combine(LogDir, file), line + Environment.NewLine);
            }
        }
        catch { }
    }
}
