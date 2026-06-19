using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Game;
using FourRVivi.Core.Ocr;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;
using FourRVivi.App.Overlay;

namespace FourRVivi.App.ViewModels;

/// <summary>OCR reader: calibrate once by marking a screenshot, then read those regions off the live
/// window every tick and feed LiveStats (top bar, Stats tab, Discord). No memory access — Gepard-proof.</summary>
public sealed partial class OcrReaderViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly OcrService _ocr;
    private readonly SettingsStore _settings;
    private System.Threading.Timer? _timer;
    private volatile bool _busy;
    private List<OcrMark> _activeMarks = new();
    private readonly DiscordPresenceUpdater _discord;
    private GlobalKeyHook? _hook;
    private OcrOverlayWindow? _overlay;
    private readonly Dictionary<string, (string val, int count)> _pending = new();
    [ObservableProperty] private int _intervalMs = 300;
    public string[] WindowModes { get; } = { "Fullscreen", "Windowed" };
    [ObservableProperty] private string _windowMode = "Windowed";
    [ObservableProperty] private int _topOffset = 31;   // title bar height (windowed)
    [ObservableProperty] private int _sideOffset = 8;   // window border (windowed)
    [ObservableProperty] private bool _overlayOn;
    [ObservableProperty] private string _overlayHotkey = "F8";
    [ObservableProperty] private string _ocrHotkey = "F9";

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    [ObservableProperty] private double _zoom = 1.0;
    public string[] PreprocessModes { get; } = { "Auto", "Light text", "Dark text", "Invert", "Grayscale", "High contrast", "Red", "Green", "Blue" };
    [ObservableProperty] private string _preprocessMode = "Auto";
    partial void OnPreprocessModeChanged(string value) => _ocr.PreprocessMode = string.IsNullOrEmpty(value) ? "Auto" : value;

    private int TopPx => WindowMode == "Windowed" ? TopOffset : 0;
    private int SidePx => WindowMode == "Windowed" ? SideOffset : 0;

    // role keys the user can mark (CharName is text; the rest are numbers)
    public string[] Roles { get; } =
    {
        "BasicInfo",
        "HP / MaxHP", "SP / MaxSP", "Weight / MaxWeight",
        "BaseLevel", "JobLevel",
        "HP", "MaxHP", "SP", "MaxSP",
        "HpPercent", "SpPercent", "BaseExpBar", "JobExpBar",
        "Weight", "MaxWeight", "Zeny", "Loot", "PosX", "PosY", "CharName", "ClassName"
    };

    private static readonly Dictionary<string, (string a, string b)> Combined = new()
    {
        ["HP / MaxHP"] = ("HP", "MaxHP"),
        ["SP / MaxSP"] = ("SP", "MaxSP"),
        ["Weight / MaxWeight"] = ("Weight", "MaxWeight"),
    };

    public ObservableCollection<OcrMark> Marks { get; } = new();
    public ObservableCollection<string> LiveReadout { get; } = new();
    [ObservableProperty] private string _selectedRole = "HP";
    [ObservableProperty] private bool _running;
    [ObservableProperty] private string _status = "1) Load a screenshot. 2) Pick a stat, drag a box over it. 3) Save. 4) Start.";

    public OcrReaderViewModel(GameSession session, OcrService ocr, SettingsStore settings, DiscordPresenceUpdater discord)
    {
        _session = session; _ocr = ocr; _settings = settings; _discord = discord;
        foreach (var m in settings.Current.OcrMarks) Marks.Add(m);
        try { _hook = new GlobalKeyHook(); _hook.KeyPressed += OnGlobalKey; } catch { }
    }

    private void OnGlobalKey(int vk)
    {
        // ignore modifier-only keys (we read their state instead)
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0x5B or 0x5C) return;
        string name = KeyName(vk);
        if (name.Length == 0) return;
        string combo = (Down(0x11) ? "Ctrl+" : "") + (Down(0x12) ? "Alt+" : "") + (Down(0x10) ? "Shift+" : "") + name;
        if (Match(combo, OverlayHotkey)) Dispatcher.UIThread.Post(() => ToggleOverlayCommand.Execute(null));
        else if (Match(combo, OcrHotkey)) Dispatcher.UIThread.Post(ToggleOcr);
    }

    private static bool Match(string combo, string assigned)
        => !string.IsNullOrWhiteSpace(assigned) && string.Equals(combo, assigned.Trim(), StringComparison.OrdinalIgnoreCase);

    private void ToggleOcr() { if (Running) Stop(); else StartCommand.Execute(null); }

    private static string KeyName(int vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);              // F1-F24
        if (vk >= 0x60 && vk <= 0x69) return "NumPad" + (vk - 0x60);          // NumPad0-9
        return vk switch
        {
            0x20 => "Space", 0x0D => "Enter", 0x09 => "Tab", 0x2D => "Insert", 0x2E => "Delete",
            0x24 => "Home", 0x23 => "End", 0x21 => "PageUp", 0x22 => "PageDown",
            0x6A => "Multiply", 0x6B => "Add", 0x6D => "Subtract", 0x6F => "Divide",
            _ => ""
        };
    }

    /// <summary>Called by the view when the user finishes dragging a box (fractions 0..1).</summary>
    public void AddMark(string role, double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        bool isText = role == "CharName" || role == "ClassName" || role == "BasicInfo";
        bool isBar = role is "HpPercent" or "SpPercent" or "BaseExpBar" or "JobExpBar";
        Marks.Add(new OcrMark { Role = role, X = x, Y = y, W = w, H = h, IsText = isText, IsBar = isBar });
        Status = $"Marked {role}. Mark the rest, then Save.";
    }

    [RelayCommand] private void RemoveMark(OcrMark? m) { if (m != null) Marks.Remove(m); }
    [RelayCommand] private void ClearMarks() { Marks.Clear(); Status = "Cleared. Re-mark your stats."; }

    [RelayCommand] private void Save()
    {
        _settings.Current.OcrMarks = Marks.ToList();
        _settings.Save();
        Status = $"Saved {Marks.Count} marks.";
    }

    [RelayCommand] private async Task Start()
    {
        if (Marks.Count == 0) { Status = "Mark at least HP first."; return; }
        if (_session.WindowHandle == IntPtr.Zero) { Status = "Pick your RO process in the top bar and keep the game visible."; return; }
        Status = "Preparing OCR (first run downloads language data)…";
        if (!await _ocr.EnsureDataAsync()) { Status = "Couldn't get OCR data (no internet?)."; return; }

        _pending.Clear();
        _activeMarks = Marks.ToList();
        LiveStats.Instance.Active = true;
        Running = true;
        OverlayOn = true;
        ShowOverlay();
        try { DiscordPresenceBootstrap.Apply(_discord, _session, _settings.Current); } catch { }   // ensure Discord gets OCR data
        int period = Math.Max(120, IntervalMs);
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => BgTick(), null, 250, period);   // OFF the UI thread
        Status = $"OCR running every {period}ms (background) — keep the marked stats visible. Top bar, Stats and Discord now use it.";
    }

    [RelayCommand] private void Stop()
    {
        _timer?.Dispose(); _timer = null;
        Running = false;
        LiveStats.Instance.Active = false;
        OverlayOn = false;
        HideOverlay();
        Status = "OCR stopped. Back to memory mode.";
    }

    [RelayCommand] private void ToggleOverlay()
    {
        OverlayOn = !OverlayOn;
        if (OverlayOn) ShowOverlay(); else HideOverlay();
    }

    private void ShowOverlay()
    {
        try
        {
            _overlay ??= new OcrOverlayWindow(_session);
            _overlay.SetInfo(Marks.ToList(), _session.Reader.Target?.ProcessName ?? "client", TopPx, SidePx);
            _overlay.Show();
        }
        catch { _overlay = null; }
    }

    private void HideOverlay()
    {
        try { _overlay?.Close(); } catch { }
        _overlay = null;
    }

    private void BgTick()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var hwnd = _session.WindowHandle;
            if (hwnd == IntPtr.Zero) { Dispatcher.UIThread.Post(() => Status = "Lost the game window — is it still open?"); return; }

            LiveStats.Instance.Touch();   // heartbeat so consumers stay "live" even if a read fails
            using var frame = _ocr.CaptureWindow(hwnd);   // one PrintWindow capture, crop each region from it
            var readout = new List<string>();
            foreach (var m in _activeMarks)
            {
                if (m.IsBar)
                {
                    int pct = frame != null ? _ocr.ReadBarPercentFrom(frame, m.X, m.Y, m.W, m.H, TopPx, SidePx) : _ocr.ReadBarPercent(hwnd, m.X, m.Y, m.W, m.H, TopPx, SidePx);
                    if (pct >= 0) LiveStats.Instance.SetNumber(m.Role, pct);
                    readout.Add($"{m.Role} = {(pct < 0 ? "?" : pct + "%")}");
                    continue;
                }
                if (Combined.TryGetValue(m.Role, out var pair))
                {
                    string two = frame != null ? _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, true, TopPx, SidePx) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: true, topOffset: TopPx, sideOffset: SidePx);
                    var parsed = OcrService.ParseTwoInts(two);
                    if (parsed is { } pv) { LiveStats.Instance.SetNumber(pair.a, pv.Item1); LiveStats.Instance.SetNumber(pair.b, pv.Item2); }
                    readout.Add($"{m.Role} = {(parsed is { } q ? $"{q.Item1} / {q.Item2}" : "? [" + two.Trim() + "]")}");
                    continue;
                }
                string raw = frame != null ? _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, !m.IsText, TopPx, SidePx) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: !m.IsText, topOffset: TopPx, sideOffset: SidePx);
                if (m.IsText)
                {
                    string t = raw.Trim();
                    if (t.Length > 0 && Stable(m.Role, t)) LiveStats.Instance.SetText(m.Role, t);
                    readout.Add($"{m.Role} = {(t.Length == 0 ? "?" : t)}");
                }
                else
                {
                    int n = OcrService.ParseFirstInt(raw);
                    if (n >= 0 && Stable(m.Role, n.ToString())) LiveStats.Instance.SetNumber(m.Role, n);
                    readout.Add($"{m.Role} = {(n < 0 ? "? [" + raw.Trim() + "]" : n.ToString())}");
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                LiveReadout.Clear();
                foreach (var s2 in readout) LiveReadout.Add(s2);
                if (_overlay != null) _overlay.SetInfo(Marks.ToList(), _session.Reader.Target?.ProcessName ?? "client", TopPx, SidePx);
            });
        }
        catch { }
        finally { _busy = false; }
    }

    /// <summary>Commit a value only after it reads the same twice in a row — kills OCR flicker.</summary>
    private bool Stable(string role, string val)
    {
        if (_pending.TryGetValue(role, out var p) && p.val == val) _pending[role] = (val, p.count + 1);
        else _pending[role] = (val, 1);
        return _pending[role].count >= 2;
    }
}
