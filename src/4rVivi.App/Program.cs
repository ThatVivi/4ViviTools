using Avalonia;
using FourRVivi.App.Services;

namespace FourRVivi.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Init();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { AppLog.Crash("Fatal at startup", ex); throw; }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
