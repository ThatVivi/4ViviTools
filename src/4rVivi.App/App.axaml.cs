using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Data;
using FourRVivi.Core.Trackers;
using FourRVivi.Core.Game;
using FourRVivi.Core.Discord;
using FourRVivi.Core.Localization;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;
using FourRVivi.App.ViewModels;
using FourRVivi.App.Views;

namespace FourRVivi.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static bool _shutdownDone;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
      try
      {
        Services = ConfigureServices();
        var iconSvc = Services.GetRequiredService<IconImageService>();
        IconImageService.Instance = iconSvc;
        iconSvc.ItemNet = Services.GetRequiredService<FourRVivi.Core.Trackers.ItemIconService>();
        iconSvc.SkillNet = Services.GetRequiredService<FourRVivi.Core.Trackers.SkillIconService>();
        var dbLazy = Services.GetRequiredService<Lazy<GameDatabase>>();
        iconSvc.NameToId = n => { try { return dbLazy.Value.IconId(n); } catch { return 0; } };
        iconSvc.SkillByName = n => { try { var s = dbLazy.Value.SkillByName(n); return s == null ? null : (s.Aegis, s.Id); } catch { return null; } };
        var st = Services.GetRequiredService<SettingsStore>();
        // Use the configured game folder, or auto-detect an extracted GRF tree (…/GRF/data/texture/유저인터페이스).
        var gameFolder = st.Current.GameFolder;
        if (string.IsNullOrWhiteSpace(gameFolder) || !System.IO.Directory.Exists(System.IO.Path.Combine(gameFolder, "data", "texture", "유저인터페이스")))
        {
            var found = FindGrfFolder();
            if (found != null) { gameFolder = found; st.Current.GameFolder = found; try { st.Save(); } catch { } }
        }
        iconSvc.SetGameFolder(gameFolder);
        iconSvc.SetGrf(st.Current.GrfPath);
        ViewModels.SettingsViewModel.ApplyTheme(st.Current.Theme);   // light/dark from settings
        ModelManifestLogger.LogOnce();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            vm.AttachWindow(desktop.MainWindow);
            desktop.ShutdownRequested += (_, _) => ShutdownEverything("ShutdownRequested");
            desktop.Exit += (_, _) => ShutdownEverything("Exit");
        }

        StartDiscordPresence();
        base.OnFrameworkInitializationCompleted();
      }
      catch (Exception ex) { FourRVivi.App.Services.AppLog.Crash("Startup failed", ex); throw; }
    }

    /// <summary>Walk up from the app dir looking for an extracted GRF tree. Returns the folder that
    /// contains <c>data/texture/유저인터페이스</c> (the value SetGameFolder expects), or null.</summary>
    private static string? FindGrfFolder()
    {
        const string marker = "data/texture/유저인터페이스";
        try
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                // <dir>/data/texture/...  -> game folder is <dir>
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, marker.Replace('/', System.IO.Path.DirectorySeparatorChar))))
                    return dir.FullName;
                // <dir>/GRF/data/texture/... -> game folder is <dir>/GRF
                var grf = System.IO.Path.Combine(dir.FullName, "GRF");
                if (System.IO.Directory.Exists(System.IO.Path.Combine(grf, marker.Replace('/', System.IO.Path.DirectorySeparatorChar))))
                    return grf;
            }
        }
        catch { }
        return null;
    }

    private static void StartDiscordPresence()
    {
        try
        {
            var gs = Services.GetRequiredService<GameSession>();
            var updater = Services.GetRequiredService<DiscordPresenceUpdater>();
            var settings = Services.GetRequiredService<SettingsStore>();
            DiscordPresenceBootstrap.Apply(updater, gs, settings.Current);
        }
        catch (Exception ex) { FourRVivi.App.Services.AppLog.Crash("Discord presence", ex); }
    }

    private static void ShutdownEverything(string reason)
    {
        if (_shutdownDone || Services == null) return;
        _shutdownDone = true;
        try
        {
            FourRVivi.Core.Common.DebugTrace.Write("App", $"Shutdown cleanup started ({reason}).");
            try { Services.GetService<MainWindowViewModel>()?.Shutdown(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "MainWindow shutdown failed.", ex); }
            try { Services.GetService<OcrReaderViewModel>()?.Shutdown(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "OCR shutdown failed.", ex); }
            try { Services.GetService<OverlayController>()?.Dispose(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "Overlay shutdown failed.", ex); }
            try { Services.GetService<EngineHub>()?.Shutdown(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "Engine shutdown failed.", ex); }
            try { Services.GetService<SmartBotTrainingRecorder>()?.Dispose(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "Smart Bot training shutdown failed.", ex); }
            try { Services.GetService<DiscordPresenceUpdater>()?.Dispose(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "Discord shutdown failed.", ex); }
            try { LiveScene.Instance.Clear(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "LiveScene clear failed.", ex); }
            try { KillOwnedHelperProcesses(); } catch (Exception ex) { FourRVivi.Core.Common.DebugTrace.Write("App", "Helper process cleanup failed.", ex); }
            if (Services is IDisposable d)
                try { d.Dispose(); } catch { }
            FourRVivi.Core.Common.DebugTrace.Write("App", "Shutdown cleanup finished.");
        }
        catch (Exception ex)
        {
            FourRVivi.App.Services.AppLog.Crash("Shutdown cleanup failed", ex);
        }
    }

    private static void KillOwnedHelperProcesses()
    {
        string root = "";
        try { root = System.IO.Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar; } catch { }
        foreach (var name in new[] { "4rVivi.OcrServer", "VIIPERServer", "ViiperServer", "FakerInputServer" })
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    string path = "";
                    try { path = p.MainModule?.FileName ?? ""; } catch { }
                    bool owned = string.IsNullOrEmpty(root)
                        || (!string.IsNullOrWhiteSpace(path) && System.IO.Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase));
                    if (!owned)
                        continue;
                    FourRVivi.Core.Common.DebugTrace.Write("App", $"Killing helper process name={name} pid={p.Id} path='{path}'.");
                    p.Kill(entireProcessTree: true);
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var s = new ServiceCollection();

        // Core singletons
        s.AddSingleton<SettingsStore>();
        s.AddSingleton<Loc>();
        s.AddSingleton(_ => new Lazy<GameDatabase>(() => new GameDatabase()));
        s.AddSingleton<MvpTracker>();
        s.AddSingleton(sp =>
        {
            var t = new SessionTracker(sp.GetRequiredService<GameSession>());
            t.Loot = sp.GetRequiredService<LootLog>();
            return t;
        });
        s.AddSingleton<LootLog>();
        s.AddSingleton<MvpIconService>();
        s.AddSingleton<FourRVivi.Core.Trackers.ItemIconService>();
        s.AddSingleton<FourRVivi.Core.Trackers.SkillIconService>();
        s.AddSingleton<IconService>();
        s.AddSingleton<ClassData>();
        s.AddSingleton<IconImageService>();
        s.AddSingleton<OcrService>();
        s.AddSingleton<GameSession>();
        s.AddSingleton<EngineHub>();
        s.AddSingleton<ProcessWatcher>();
        s.AddSingleton<FourRVivi.Core.Events.IEventBus, FourRVivi.Core.Events.EventBus>();
        s.AddSingleton<FourRVivi.Core.Services.IUpdateService, FourRVivi.Core.Services.UpdateService>();
        s.AddSingleton<ScreenshotService>();
        s.AddSingleton<IPluginLoader, PluginLoader>();
        s.AddSingleton<FourRVivi.Core.Configuration.IConfigService, FourRVivi.Core.Configuration.ConfigService>();
        s.AddSingleton<FourRVivi.Core.Services.IProfileService, FourRVivi.Core.Services.ProfileService>();
        s.AddSingleton<NotificationService>();
        s.AddSingleton<FourRVivi.Core.Servers.ServerProfileDb>();
        s.AddSingleton<FourRVivi.Core.Servers.ServerBinder>();
        s.AddSingleton<FourRVivi.Core.Signatures.ProfileStore>();
        s.AddSingleton<FourRVivi.Core.Signatures.SignatureBinder>();
        s.AddSingleton<DiscordService>();
        s.AddSingleton<DiscordPresenceUpdater>();

        // App services
        s.AddSingleton<ProcessService>();
        s.AddSingleton<OverlayController>();
        s.AddSingleton<NavigationService>();
        s.AddSingleton<SmartBotTrainingRecorder>();

        // ViewModels
        s.AddSingleton<MainWindowViewModel>();
        s.AddSingleton<DashboardViewModel>();
        s.AddSingleton<AutopotViewModel>();
        s.AddSingleton<BuffsViewModel>();
        s.AddSingleton<SkillsViewModel>();
        s.AddSingleton<BotFarmViewModel>();
        s.AddSingleton<MacrosViewModel>();
        s.AddSingleton<OverlayViewModel>();
        s.AddSingleton<DatabaseViewModel>();
        s.AddSingleton<ScannerViewModel>();
        s.AddSingleton<ServersViewModel>();
        s.AddSingleton<StatsViewModel>();
        s.AddSingleton<SettingsViewModel>();

        s.AddSingleton<SmartBotViewModel>();
        s.AddSingleton<ClassSkillsViewModel>();
        s.AddSingleton<MvpTrackerViewModel>();
        s.AddSingleton<HudViewModel>();
        s.AddSingleton<LootViewModel>();
        s.AddSingleton<CalculatorViewModel>();
        s.AddSingleton<HomunAiViewModel>();
        s.AddSingleton<GrfViewModel>();
        s.AddSingleton<SpriteViewerViewModel>();
        s.AddSingleton<ToolsLauncherViewModel>();
        s.AddSingleton<AutoDetectViewModel>();
        s.AddSingleton<OcrReaderViewModel>();
        s.AddSingleton<BotStudioViewModel>();
        s.AddSingleton<MultiClientViewModel>();
        s.AddSingleton<AtkDefViewModel>();
        s.AddSingleton<AutoStandViewModel>();
        s.AddSingleton<AutoYggViewModel>();
        s.AddSingleton<SpammerGridViewModel>();
        s.AddSingleton<FourRToolsShellViewModel>();
        s.AddSingleton<RoToolsShellViewModel>();
        s.AddSingleton<DamageCalcViewModel>();

        return s.BuildServiceProvider();
    }
}
