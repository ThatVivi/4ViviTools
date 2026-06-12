using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using FourRVivi.Core.Localization;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly EngineHub _hub;
    private readonly ProcessService _procs;
    private readonly SettingsStore _settings;
    private readonly Loc _loc;
    private readonly NavigationService _nav;
    private readonly Dictionary<string, NavPage> _pageByKey = new();

    public ObservableCollection<object> NavItems { get; } = new();
    public ObservableCollection<GameProcess> Processes { get; } = new();
    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private GameProcess? _selectedProcess;
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private bool _masterOn;
    [ObservableProperty] private double _windowOpacity = 1.0;
    [ObservableProperty] private string _statusText = "Pick your RO process, then turn everything ON.";
    [ObservableProperty] private string _masterLabel = "Turn everything ON";
    [ObservableProperty] private double _hpPercent = -1;
    [ObservableProperty] private double _spPercent = -1;

    public MainWindowViewModel(
        GameSession session, EngineHub hub, ProcessService procs, SettingsStore settings, Loc loc, NavigationService nav,
        DashboardViewModel dashboard, AutopotViewModel autopot, BuffsViewModel buffs, SkillsViewModel skills,
        SmartBotViewModel smartBot, BotFarmViewModel botFarm, OverlayViewModel overlay, MacrosViewModel macros,
        MvpTrackerViewModel mvp, HudViewModel hud, LootViewModel loot,
        DatabaseViewModel database, ItemDbEditorViewModel itemDb, CalculatorViewModel calc, SnippetsViewModel snippets, HomunAiViewModel homun,
        GrfViewModel grf, SpriteViewerViewModel sprite, ToolsLauncherViewModel tools,
        ScannerViewModel scanner, ServersViewModel servers, StatsViewModel stats, SettingsViewModel settingsVm)
    {
        _session = session; _hub = hub; _procs = procs; _settings = settings; _loc = loc; _nav = nav;

        BuildNav(new[] { Page("Dashboard", dashboard) });

        AddSection("COMBAT");
        AddPage("Autopot", autopot);
        AddPage("Buffs", buffs);
        AddPage("Skills", skills);
        AddPage("Smart Bot", smartBot);
        AddPage("Bot (basic)", botFarm);
        AddPage("RCX Overlay", overlay);

        AddSection("TRACKERS");
        AddPage("MVP Tracker", mvp);
        AddPage("Buff HUD", hud);
        AddPage("Loot Log", loot);

        AddSection("MACROS");
        AddPage("Macros", macros);

        AddSection("DATA");
        AddPage("Database", database);
        AddPage("Item-DB Editor", itemDb);
        AddPage("Calculator", calc);
        AddPage("NPC Snippets", snippets);
        AddPage("Homun AI", homun);

        AddSection("TOOLS");
        AddPage("GRF Browser", grf);
        AddPage("Sprite Viewer", sprite);
        AddPage("External Editors", tools);

        AddSection("SYSTEM");
        AddPage("Scanner", scanner);
        AddPage("Servers", servers);
        AddPage("Stats", stats);
        AddPage("Settings", settingsVm);

        var s = _settings.Current;
        WindowOpacity = Math.Clamp(s.WindowOpacity, 70, 100) / 100.0;
        _loc.SetLang(s.Language);
        _hub.Timing.Enabled = s.HumanizeTiming;

        foreach (var p in s.Profiles) Profiles.Add(p.Name);
        SelectedProfile = s.ActiveProfile;

        _hub.Status += msg => Dispatcher.UIThread.Post(() => StatusText = msg);
        _nav.NavigationRequested += GoToKey;
        _nav.MasterToggleRequested += () => MasterOn = !MasterOn;

        RefreshProcesses();
        TryAutoAttach();
        _hub.StartAllLoops();

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) => { HpPercent = _session.Health.HpPercent; SpPercent = _session.Health.SpPercent; };
        t.Start();
    }

    private NavPage Page(string title, ViewModelBase vm) => new(title, title, vm, OnPageSelected);

    private void BuildNav(IEnumerable<NavPage> firstPages)
    {
        foreach (var p in firstPages) { NavItems.Add(p); _pageByKey[p.Key] = p; }
        var first = NavItems.OfType<NavPage>().FirstOrDefault();
        if (first is not null) OnPageSelected(first);
    }
    private void AddSection(string title) => NavItems.Add(new NavSection(_loc.T(title)));
    private void AddPage(string title, ViewModelBase vm)
    {
        var p = new NavPage(_loc.T(title), title, vm, OnPageSelected);
        NavItems.Add(p); _pageByKey[title] = p;
    }

    private void OnPageSelected(NavPage page)
    {
        foreach (var n in NavItems.OfType<NavPage>()) n.IsActive = ReferenceEquals(n, page);
        CurrentPage = page.ViewModel;
    }
    private void GoToKey(string key) { if (_pageByKey.TryGetValue(key, out var p)) OnPageSelected(p); }

    [RelayCommand] private void RefreshProcesses()
    {
        Processes.Clear();
        var prefer = _settings.Current.GetActiveProfile().PreferredProcessNames;
        foreach (var p in _procs.List(prefer)) Processes.Add(p);
    }

    private void TryAutoAttach()
    {
        if (SelectedProcess is not null || Processes.Count == 0) return;
        var prefer = _settings.Current.GetActiveProfile().PreferredProcessNames
            .Select(n => n.ToLowerInvariant()).ToHashSet();
        var match = Processes.FirstOrDefault(p => prefer.Contains(p.Name.ToLowerInvariant()));
        if (match is not null) SelectedProcess = match;
    }

    partial void OnSelectedProcessChanged(GameProcess? value)
    {
        if (value is null) return;
        var r = _procs.Attach(value);
        StatusText = r.Ok ? $"Attached to {value.Name}.exe ({(_session.Reader.TargetIs64Bit() ? "64-bit" : "32-bit")})." : r.Error!;
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var prof = _settings.Current.Profiles.FirstOrDefault(p => p.Name == value);
        if (prof is null) return;
        _settings.Current.ActiveProfile = value;
        _session.UseProfile(value, prof.Addresses);
        _settings.Save();
    }

    partial void OnMasterOnChanged(bool value)
    {
        _session.SetMaster(value);
        MasterLabel = value ? "Turn everything OFF" : "Turn everything ON";
        StatusText = value ? "Master ON — enabled features are running." : "Master OFF.";
    }

    public void AttachWindow(Window w)
    {
        w.KeyDown += (_, e) => { if (e.Key == Key.F12) MasterOn = false; };
    }

    [RelayCommand] private void ToggleMaster() => MasterOn = !MasterOn;
}
