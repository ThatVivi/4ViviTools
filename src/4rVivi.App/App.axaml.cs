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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
      try
      {
        Services = ConfigureServices();
        var iconSvc = Services.GetRequiredService<IconImageService>();
        IconImageService.Instance = iconSvc;
        var st = Services.GetRequiredService<SettingsStore>();
        iconSvc.SetGameFolder(st.Current.GameFolder);
        iconSvc.SetGrf(st.Current.GrfPath);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            vm.AttachWindow(desktop.MainWindow);
        }

        StartDiscordPresence();
        base.OnFrameworkInitializationCompleted();
      }
      catch (Exception ex) { FourRVivi.App.Services.AppLog.Crash("Startup failed", ex); throw; }
    }

    private static void StartDiscordPresence()
    {
        try
        {
            var d = Program.Settings.Discord;
            if (d is null || !d.Enabled || string.IsNullOrWhiteSpace(d.AppId)) return;

            var gs = Services.GetRequiredService<GameSession>();
            var reader = new CharacterStateReader(gs);
            Services.GetRequiredService<DiscordPresenceUpdater>().Start(d.AppId, () =>
            {
                var cs = reader.Snapshot();
                if (cs is null) return null;   // not attached -> no presence
                return new RoPresence
                {
                    CharName   = cs.Name,
                    ClassName  = cs.ClassName,
                    BaseLevel  = cs.BaseLevel,
                    JobLevel   = cs.JobLevel,
                    MapName    = cs.MapName,
                    X = cs.X, Y = cs.Y,
                    HpPct = cs.HpPct, SpPct = cs.SpPct,
                    Activity = cs.Activity,
                    ServerName = d.ServerName,
                    WebsiteUrl = d.WebsiteUrl,
                    LargeImageKey = d.LargeImageKey,
                };
            }, d.IntervalSeconds);
        }
        catch (Exception ex) { FourRVivi.App.Services.AppLog.Crash("Discord presence", ex); }
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
        s.AddSingleton<DiscordService>();
        s.AddSingleton<DiscordPresenceUpdater>();

        // App services
        s.AddSingleton<ProcessService>();
        s.AddSingleton<OverlayController>();
        s.AddSingleton<NavigationService>();

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
        s.AddSingleton<MarketplaceViewModel>();

        return s.BuildServiceProvider();
    }
}
