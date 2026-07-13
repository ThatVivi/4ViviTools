using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Common;
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
    private readonly SmartBotViewModel _smartBot;
    private readonly Dictionary<string, NavPage> _pageByKey = new();
    private Window? _window;
    private IntPtr _windowHandle;
    private Win32Properties.CustomWndProcHookCallback? _wndProcHook;
    private DispatcherTimer? _globalKeyPollTimer;
    private bool _panicWasDown;
    private bool _smartBotToggleWasDown;
    private bool _smartBotStartWasDown;
    private bool _smartBotStopWasDown;
    private const int PanicHotkeyId = 4012;
    private const int SmartBotHotkeyId = 4013;
    private const int SmartBotStartHotkeyId = 4014;
    private const int SmartBotStopHotkeyId = 4015;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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
        ScannerViewModel scanner, AutoDetectViewModel autoDetect, OcrReaderViewModel ocrReader, BotStudioViewModel botStudio, MultiClientViewModel multiClient, ServersViewModel servers, StatsViewModel stats, SettingsViewModel settingsVm,
        FourRToolsShellViewModel fourRTools, RoToolsShellViewModel roTools, DamageCalcViewModel damageCalc)
    {
        _session = session; _hub = hub; _procs = procs; _settings = settings; _loc = loc; _nav = nav;
        _smartBot = smartBot;

        AddCat("Home", ("Dashboard", dashboard));
        AddCat("Bot", ("Smart Bot", smartBot), ("Multi Client", multiClient), ("OCR Reader", ocrReader), ("Overlay", overlay), ("Macros", macros));
        AddCat("Trackers", ("MVP", mvp), ("Buff HUD", hud), ("Loot Log", loot), ("Stats", stats));
        AddCat("Data", ("Calculator", calc), ("Database", database));
        AddCat("Tools", ("GRF", grf), ("Sprite", sprite), ("Homun AI", homun), ("External Editors", tools), ("Legacy 4rTools", fourRTools), ("Legacy ro-tools", roTools));
        AddCat("System", ("Auto-Detect", autoDetect), ("Servers", servers), ("Settings", settingsVm));
        if (Categories.Count > 0) OnCategorySelected(Categories[0]);

        var s = _settings.Current;
        WindowOpacity = Math.Clamp(s.WindowOpacity, 15, 100) / 100.0;
        _loc.SetLang(s.Language);
        _hub.Timing.Enabled = s.HumanizeTiming;

        foreach (var p in s.Profiles) Profiles.Add(p.Name);
        SelectedProfile = s.ActiveProfile;

        _hub.Status += msg => Dispatcher.UIThread.Post(() => StatusText = msg);
        _nav.NavigationRequested += GoToKey;
        _nav.MasterToggleRequested += () => MasterOn = !MasterOn;
        _smartBot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SmartBotViewModel.ToggleHotkey) ||
                e.PropertyName == nameof(SmartBotViewModel.StartHotkey) ||
                e.PropertyName == nameof(SmartBotViewModel.StopHotkey))
                RegisterGlobalHotkeys();
        };

        RefreshProcesses();
        TryAutoAttach();
        _hub.StartAllLoops();

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) =>
        {
            var h = _session.Health;
            HpPercent = h.HpPercent; SpPercent = h.SpPercent;
            HpText = HpPercent >= 0 ? $"{HpPercent:0}%" : "";
            SpText = SpPercent >= 0 ? $"{SpPercent:0}%" : "";
        };
        t.Start();
    }

    private void AddCat(string title, params (string title, ViewModelBase vm)[] pages)
    {
        var cat = new NavCategory(_loc.T(title), title, OnCategorySelected, IconFor(title));
        foreach (var (t, vm) in pages)
        {
            var p = new NavPage(_loc.T(t), t, vm, OnPageSelected, IconFor(t));
            cat.Pages.Add(p); _pageByKey[t] = p;
        }
        Categories.Add(cat);
    }

    private void AddCloneCat(string title, string key, params (string key, string title, ViewModelBase vm, Action<bool>? toggle)[] pages)
    {
        var cat = new NavCategory(_loc.T(title), key, OnCategorySelected, IconFor(title)) { Togglable = true };
        foreach (var (k, t, vm, toggle) in pages)
        {
            var p = new NavPage(_loc.T(t), k, vm, OnPageSelected, IconFor(t));
            cat.Pages.Add(p); _pageByKey[k] = p;
            if (toggle != null) cat.Toggles.Add(toggle);
        }
        Categories.Add(cat);
    }

    private static string IconFor(string key) => key switch
    {
        "Home" or "Dashboard" => "\uE80F",
        "Bot" or "Smart Bot" => "\uE8FB",
        "Multi Client" => "\uE8A9",
        "OCR Reader" => "\uE8B3",
        "Overlay" => "\uE7F4",
        "Macros" => "\uE8D4",
        "Trackers" or "Stats" => "\uE9D2",
        "MVP" => "\uE7C1",
        "Buff HUD" or "Buffs" => "\uE95E",
        "Loot Log" => "\uE8CB",
        "Data" or "Database" => "\uE8E5",
        "Calculator" => "\uE8EF",
        "Tools" => "\uE90F",
        "GRF" => "\uE8B7",
        "Sprite" => "\uE91B",
        "Homun AI" => "\uE7B8",
        "External Editors" => "\uE70F",
        "Legacy 4rTools" => "\uE7B3",
        "Legacy ro-tools" => "\uE7B3",
        "System" or "Settings" => "\uE713",
        "Auto-Detect" => "\uE8B3",
        "Servers" => "\uE968",
        _ => "\uE8A5",
    };

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
        DebugTrace.Write("Attach", $"SelectedProcess name={value.Name} pid={value.Pid} hwnd=0x{value.WindowHandle.ToInt64():X} ok={r.Ok} status='{StatusText}'.");
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
        StatusText = value ? "Master ON - enabled features are running." : "Master OFF.";
    }

    public void AttachWindow(Window w)
    {
        _window = w;
        w.Opened += (_, _) => InstallGlobalHotkeys(w);
        w.Closed += (_, _) => UninstallGlobalHotkeys();
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
            {
                StopAll();
                e.Handled = true;
                return;
            }

            if (IsCaptureBoxEvent(e))
                return;

            if (MatchesHotkey(e, _smartBot.StartHotkey))
            {
                StartSmartBotFromHotkey(_smartBot.StartHotkey);
                e.Handled = true;
                return;
            }

            if (MatchesHotkey(e, _smartBot.StopHotkey))
            {
                StopSmartBotFromHotkey(_smartBot.StopHotkey);
                e.Handled = true;
                return;
            }

            if (MatchesHotkey(e, _smartBot.ToggleHotkey))
            {
                _smartBot.ToggleBotFromHotkey();
                StatusText = _smartBot.Enabled
                    ? $"Smart Bot started ({_smartBot.ToggleHotkey})."
                    : $"Smart Bot stopped ({_smartBot.ToggleHotkey}).";
                e.Handled = true;
            }
        };
    }

    [RelayCommand] private void StopAll()
    {
        DebugTrace.Write("Hotkey", "StopAll invoked.");
        _hub.DisableAll();
        _smartBot.Enabled = false;
        MasterOn = false;
        StatusText = "All features stopped (F12).";
    }

    public void Shutdown()
    {
        DebugTrace.Write("MainWindowVM", "Shutdown requested.");
        StopGlobalKeyPolling();
        UninstallGlobalHotkeys();
        _hub.DisableAll();
        _smartBot.Enabled = false;
        MasterOn = false;
        StatusText = "4ViviTools is shutting down. All automation is off.";
    }

    private void InstallGlobalHotkeys(Window w)
    {
        _windowHandle = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_windowHandle == IntPtr.Zero)
        {
            DebugTrace.Write("Hotkey", "InstallGlobalHotkeys failed: window handle is zero.");
            return;
        }

        _wndProcHook = WndProcHook;
        Win32Properties.AddWndProcHookCallback(w, _wndProcHook);
        DebugTrace.Write("Hotkey", $"Installed WndProc hook hwnd=0x{_windowHandle.ToInt64():X}.");
        RegisterGlobalHotkeys();
        StartGlobalKeyPolling();
    }

    private void UninstallGlobalHotkeys()
    {
        StopGlobalKeyPolling();
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, PanicHotkeyId);
            UnregisterHotKey(_windowHandle, SmartBotHotkeyId);
            UnregisterHotKey(_windowHandle, SmartBotStartHotkeyId);
            UnregisterHotKey(_windowHandle, SmartBotStopHotkeyId);
            DebugTrace.Write("Hotkey", "Unregistered global hotkeys.");
        }

        if (_window != null && _wndProcHook != null)
            Win32Properties.RemoveWndProcHookCallback(_window, _wndProcHook);

        _windowHandle = IntPtr.Zero;
        _wndProcHook = null;
    }

    private void RegisterGlobalHotkeys()
    {
        if (_windowHandle == IntPtr.Zero)
            return;

        UnregisterHotKey(_windowHandle, PanicHotkeyId);
        UnregisterHotKey(_windowHandle, SmartBotHotkeyId);
        UnregisterHotKey(_windowHandle, SmartBotStartHotkeyId);
        UnregisterHotKey(_windowHandle, SmartBotStopHotkeyId);

        var panicOk = RegisterHotKey(_windowHandle, PanicHotkeyId, MOD_NOREPEAT, (uint)FourRVivi.Core.Input.KeyName.ToVk("F12"));
        DebugTrace.Write("Hotkey", $"Register panic F12 ok={panicOk} error={Marshal.GetLastWin32Error()}.");
        RegisterSmartBotHotkey(SmartBotStartHotkeyId, "start", _smartBot.StartHotkey);
        RegisterSmartBotHotkey(SmartBotStopHotkeyId, "stop", _smartBot.StopHotkey);
        if (TryParseHotkey(_smartBot.ToggleHotkey, out var modifiers, out var vk))
        {
            var toggleOk = RegisterHotKey(_windowHandle, SmartBotHotkeyId, modifiers | MOD_NOREPEAT, vk);
            DebugTrace.Write("Hotkey", $"Register SmartBot '{_smartBot.ToggleHotkey}' vk=0x{vk:X} mods=0x{modifiers:X} ok={toggleOk} error={Marshal.GetLastWin32Error()}.");
        }
        else
        {
            DebugTrace.Write("Hotkey", $"SmartBot hotkey not registered. Value='{_smartBot.ToggleHotkey}'.");
        }
    }

    private void RegisterSmartBotHotkey(int id, string label, string hotkey)
    {
        if (TryParseHotkey(hotkey, out var modifiers, out var vk))
        {
            var ok = RegisterHotKey(_windowHandle, id, modifiers | MOD_NOREPEAT, vk);
            DebugTrace.Write("Hotkey", $"Register SmartBot {label} '{hotkey}' vk=0x{vk:X} mods=0x{modifiers:X} ok={ok} error={Marshal.GetLastWin32Error()}.");
        }
        else
        {
            DebugTrace.Write("Hotkey", $"SmartBot {label} hotkey not registered. Value='{hotkey}'.");
        }
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY)
            return IntPtr.Zero;

        int id = wParam.ToInt32();
        if (id == PanicHotkeyId)
        {
            DebugTrace.Write("Hotkey", "WM_HOTKEY panic F12 received.");
            StopAll();
            handled = true;
        }
        else if (id == SmartBotHotkeyId)
        {
            DebugTrace.Write("Hotkey", $"WM_HOTKEY SmartBot toggle received. CurrentEnabled={_smartBot.Enabled}.");
            _smartBot.ToggleBotFromHotkey();
            StatusText = _smartBot.Enabled
                ? $"Smart Bot started ({_smartBot.ToggleHotkey})."
                : $"Smart Bot stopped ({_smartBot.ToggleHotkey}).";
            handled = true;
        }
        else if (id == SmartBotStartHotkeyId)
        {
            DebugTrace.Write("Hotkey", "WM_HOTKEY SmartBot start received.");
            StartSmartBotFromHotkey(_smartBot.StartHotkey);
            handled = true;
        }
        else if (id == SmartBotStopHotkeyId)
        {
            DebugTrace.Write("Hotkey", "WM_HOTKEY SmartBot stop received.");
            StopSmartBotFromHotkey(_smartBot.StopHotkey);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void StartGlobalKeyPolling()
    {
        if (_globalKeyPollTimer != null)
            return;

        _globalKeyPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _globalKeyPollTimer.Tick += (_, _) => PollGlobalKeys();
        _globalKeyPollTimer.Start();
        DebugTrace.Write("Hotkey", "Started GetAsyncKeyState fallback polling.");
    }

    private void StopGlobalKeyPolling()
    {
        if (_globalKeyPollTimer == null)
            return;

        _globalKeyPollTimer.Stop();
        _globalKeyPollTimer = null;
        _panicWasDown = false;
        _smartBotToggleWasDown = false;
        _smartBotStartWasDown = false;
        _smartBotStopWasDown = false;
        DebugTrace.Write("Hotkey", "Stopped GetAsyncKeyState fallback polling.");
    }

    private void PollGlobalKeys()
    {
        bool panicDown = IsVkDown(FourRVivi.Core.Input.KeyName.ToVk("F12"));
        if (panicDown && !_panicWasDown)
        {
            DebugTrace.Write("Hotkey", "GetAsyncKeyState panic F12 edge detected.");
            StopAll();
        }
        _panicWasDown = panicDown;

        _smartBotStartWasDown = PollSmartBotEdge(_smartBot.StartHotkey, _smartBotStartWasDown, "start", StartSmartBotFromHotkey);
        _smartBotStopWasDown = PollSmartBotEdge(_smartBot.StopHotkey, _smartBotStopWasDown, "stop", StopSmartBotFromHotkey);

        if (TryParseHotkey(_smartBot.ToggleHotkey, out var modifiers, out var vk))
        {
            bool toggleDown = IsVkDown((int)vk) && AreModifiersDown(modifiers);
            if (toggleDown && !_smartBotToggleWasDown)
            {
                DebugTrace.Write("Hotkey", $"GetAsyncKeyState SmartBot edge detected hotkey='{_smartBot.ToggleHotkey}'.");
                _smartBot.ToggleBotFromHotkey();
                StatusText = _smartBot.Enabled
                    ? $"Smart Bot started ({_smartBot.ToggleHotkey})."
                    : $"Smart Bot stopped ({_smartBot.ToggleHotkey}).";
            }
            _smartBotToggleWasDown = toggleDown;
        }
        else
        {
            _smartBotToggleWasDown = false;
        }
    }

    private bool PollSmartBotEdge(string hotkey, bool wasDown, string label, Action<string> action)
    {
        if (!TryParseHotkey(hotkey, out var modifiers, out var vk))
            return false;
        bool down = IsVkDown((int)vk) && AreModifiersDown(modifiers);
        if (down && !wasDown)
        {
            DebugTrace.Write("Hotkey", $"GetAsyncKeyState SmartBot {label} edge detected hotkey='{hotkey}'.");
            action(hotkey);
        }
        return down;
    }

    private void StartSmartBotFromHotkey(string hotkey)
    {
        _smartBot.StartBotFromHotkey(hotkey);
        StatusText = $"Smart Bot started ({hotkey}).";
    }

    private void StopSmartBotFromHotkey(string hotkey)
    {
        _smartBot.StopBotFromHotkey(hotkey);
        StatusText = $"Smart Bot stopped ({hotkey}).";
    }

    private static bool AreModifiersDown(uint modifiers)
    {
        bool wantCtrl = (modifiers & MOD_CONTROL) != 0;
        bool wantAlt = (modifiers & MOD_ALT) != 0;
        bool wantShift = (modifiers & MOD_SHIFT) != 0;
        if (wantCtrl && !IsVkDown(0x11)) return false;
        if (wantAlt && !IsVkDown(0x12)) return false;
        if (wantShift && !IsVkDown(0x10)) return false;

        if (!wantCtrl && IsVkDown(0x11)) return false;
        if (!wantAlt && IsVkDown(0x12)) return false;
        if (!wantShift && IsVkDown(0x10)) return false;
        return true;
    }

    private static bool IsVkDown(int vk) => vk > 0 && (GetAsyncKeyState(vk) & unchecked((short)0x8000)) != 0;

    private static bool IsCaptureBoxEvent(KeyEventArgs e)
    {
        var typeName = e.Source?.GetType().Name ?? "";
        return typeName.Contains("CaptureBox", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Recorder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesHotkey(KeyEventArgs e, string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        bool wantCtrl = parts.Any(p => string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase));
        bool wantAlt = parts.Any(p => string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase));
        bool wantShift = parts.Any(p => string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase));
        bool hasCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool hasAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool hasShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (wantCtrl != hasCtrl || wantAlt != hasAlt || wantShift != hasShift) return false;

        var keyPart = parts.LastOrDefault(p =>
            !string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase));
        return string.Equals(NormalizeKey(e.Key), keyPart, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseHotkey(string? hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts)
        {
            if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_CONTROL;
            else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_ALT;
            else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_SHIFT;
            else vk = (uint)FourRVivi.Core.Input.KeyName.ToVk(part);
        }

        return vk != 0;
    }

    private static string NormalizeKey(Key key)
    {
        var name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name[1].ToString();
        if (name == "Return") return "Enter";
        return name;
    }
}
