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

    public ObservableCollection<NavCategory> Categories { get; } = new();
    public ObservableCollection<GameProcess> Processes { get; } = new();
    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private NavCategory? _currentCategory;
    [ObservableProperty] private GameProcess? _selectedProcess;
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private bool _masterOn;
    [ObservableProperty] private double _windowOpacity = 1.0;
    [ObservableProperty] private string _statusText = "Pick your RO process, then turn everything ON.";
    [ObservableProperty] private string _masterLabel = "Turn everything ON";
    [ObservableProperty] private double _hpPercent = -1;
    [ObservableProperty] private double _spPercent = -1;
    [ObservableProperty] private string _hpText = "";
    [ObservableProperty] private string _spText = "";

    public MainWindowViewModel(
        GameSession session, EngineHub hub, ProcessService procs, SettingsStore settings, Loc loc, NavigationService nav,
        DashboardViewModel dashboard, AutopotViewModel autopot, BuffsViewModel buffs, SkillsViewModel skills,
        SmartBotViewModel smartBot, ClassSkillsViewModel classSkills, BotFarmViewModel botFarm, OverlayViewModel overlay, MacrosViewModel macros,
        MvpTrackerViewModel mvp, HudViewModel hud, LootViewModel loot,
        DatabaseViewModel database, CalculatorViewModel calc, HomunAiViewModel homun,
        GrfViewModel grf, SpriteViewerViewModel sprite, ToolsLauncherViewModel tools,
        ScannerViewModel scanner, AutoDetectViewModel autoDetect, OcrReaderViewModel ocrReader, ServersViewModel servers, StatsViewModel stats, SettingsViewModel settingsVm)
    {
        _session = session; _hub = hub; _procs = procs; _settings = settings; _loc = loc; _nav = nav;

        AddCat("Dashboard", ("Dashboard", dashboard));
        AddCat("OCR Reader", ("OCR Reader", ocrReader));
        AddCat("Macro", ("Macros", macros), ("Autopot", autopot), ("Buffs", buffs), ("Skills", skills), ("Skill Spammer", classSkills));
        AddCat("Bot", ("Basic", botFarm), ("Smart", smartBot), ("RCX Overlay", overlay));
        AddCat("Trackers", ("MVP", mvp), ("Buff HUD", hud), ("Loot Log", loot), ("Stats", stats));
        AddCat("Data", ("Database", database), ("Calculator", calc), ("Homun AI", homun));
        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("External Editors", tools));
        AddCat("System", ("Auto-Detect", autoDetect), ("Servers", servers), ("Settings", settingsVm));
        if (Categories.Count > 0) OnCategorySelected(Categories[0]);

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
        t.Tick += (_, _) =>
        {
            var h = _session.Health;
            HpPercent = h.HpPercent; SpPercent = h.SpPercent;
            HpText = h.Hp < 0 ? "" : (h.MaxHp > 0 ? $"{h.Hp}/{h.MaxHp}" : h.Hp.ToString());
            SpText = h.Sp < 0 ? "" : (h.MaxSp > 0 ? $"{h.Sp}/{h.MaxSp}" : h.Sp.ToString());
        };
        t.Start();
    }

    private void AddCat(string title, params (string title, ViewModelBase vm)[] pages)
    {
        var cat = new NavCategory(_loc.T(title), title, OnCategorySelected);
        foreach (var (t, vm) in pages)
        {
            var p = new NavPage(_loc.T(t), t, vm, OnPageSelected);
            cat.Pages.Add(p); _pageByKey[t] = p;
        }
        Categories.Add(cat);
    }

    private void OnCategorySelected(NavCategory cat)
    {
        foreach (var c in Categories) c.IsActive = ReferenceEquals(c, cat);
        CurrentCategory = cat;
        var first = cat.Pages.FirstOrDefault();
        if (first is not null) OnPageSelected(first);
    }

    private void OnPageSelected(NavPage page)
    {
        foreach (var c in Categories) foreach (var p in c.Pages) p.IsActive = ReferenceEquals(p, page);
        CurrentPage = page.ViewModel;
    }

    private void GoToKey(string key)
    {
        foreach (var c in Categories)
            foreach (var p in c.Pages)
                if (p.Key == key) { OnCategorySelected(c); OnPageSelected(p); return; }
    }

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
        w.KeyDown += (_, e) => { if (e.Key == Key.F12) StopAll(); };
    }

    [RelayCommand] private void StopAll() { _hub.DisableAll(); StatusText = "All features stopped (F12)."; }
}
