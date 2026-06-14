using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Data;
using FourRVivi.Core.Trackers;
using FourRVivi.Core.Game;
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
        Services = ConfigureServices();
        var iconSvc = Services.GetRequiredService<IconImageService>();
        IconImageService.Instance = iconSvc;
        iconSvc.SetGameFolder(Services.GetRequiredService<SettingsStore>().Current.GameFolder);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            vm.AttachWindow(desktop.MainWindow);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var s = new ServiceCollection();

        // Core singletons
        s.AddSingleton<SettingsStore>();
        s.AddSingleton<Loc>();
        s.AddSingleton(_ => new Lazy<GameDatabase>(() => new GameDatabase()));
        s.AddSingleton<MvpTracker>();
        s.AddSingleton(sp => new SessionTracker(sp.GetRequiredService<GameSession>()));
        s.AddSingleton<LootLog>();
        s.AddSingleton<MvpIconService>();
        s.AddSingleton<IconService>();
        s.AddSingleton<IconImageService>();
        s.AddSingleton<OcrService>();
        s.AddSingleton<GameSession>();
        s.AddSingleton<EngineHub>();
        s.AddSingleton<ProcessWatcher>();

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
        s.AddSingleton<MvpTrackerViewModel>();
        s.AddSingleton<HudViewModel>();
        s.AddSingleton<LootViewModel>();
        s.AddSingleton<CalculatorViewModel>();
        s.AddSingleton<HomunAiViewModel>();
        s.AddSingleton<GrfViewModel>();
        s.AddSingleton<SpriteViewerViewModel>();
        s.AddSingleton<ToolsLauncherViewModel>();

        return s.BuildServiceProvider();
    }
}
