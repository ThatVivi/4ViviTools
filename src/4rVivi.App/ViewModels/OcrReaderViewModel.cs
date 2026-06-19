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
    private DispatcherTimer? _timer;
    private OcrOverlayWindow? _overlay;
    private readonly Dictionary<string, (string val, int count)> _pending = new();
    [ObservableProperty] private int _intervalMs = 300;
    public string[] WindowModes { get; } = { "Fullscreen", "Windowed" };
    [ObservableProperty] private string _windowMode = "Windowed";
    [ObservableProperty] private int _topOffset = 31;   // title bar height (windowed)
    [ObservableProperty] private int _sideOffset = 8;   // window border (windowed)
    [ObservableProperty] private bool _overlayOn;
    [ObservableProperty] private string _overlayHotkey = "F8";
    [ObservableProperty] private double _zoom = 1.0;

    private int TopPx => WindowMode == "Windowed" ? TopOffset : 0;
    private int SidePx => WindowMode == "Windowed" ? SideOffset : 0;

    // role keys the user can mark (CharName is text; the rest are numbers)
    public string[] Roles { get; } =
    {
        "HP / MaxHP", "SP / MaxSP", "Weight / MaxWeight",
        "BaseLevel", "JobLevel",
        "HP", "MaxHP", "SP", "MaxSP",
        "HpPercent", "SpPercent", "BaseExpPct", "JobExpPct",
        "Weight", "MaxWeight", "Zeny", "BaseEXP", "JobEXP", "Loot", "PosX", "PosY", "CharName", "ClassName"
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

    public OcrReaderViewModel(GameSession session, OcrService ocr, SettingsStore settings)
    {
        _session = session; _ocr = ocr; _settings = settings;
        foreach (var m in settings.Current.OcrMarks) Marks.Add(m);
    }

    /// <summary>Called by the view when the user finishes dragging a box (fractions 0..1).</summary>
    public void AddMark(string role, double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        bool isText = role == "CharName" || role == "ClassName";
        bool isBar = role is "HpPercent" or "SpPercent" or "BaseExpPct" or "JobExpPct";
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
        LiveStats.Instance.Active = true;
        Running = true;
        OverlayOn = true;
        ShowOverlay();
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(120, IntervalMs)) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Status = $"OCR running every {Math.Max(120, IntervalMs)}ms — keep the marked stats visible. Top bar, Stats and Discord now use it.";
    }

    [RelayCommand] private void Stop()
    {
        _timer?.Stop();
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

    private void Tick()
    {
        var hwnd = _session.WindowHandle;
        if (hwnd == IntPtr.Zero) { Status = "Lost the game window — is it still open?"; return; }
        if (_overlay != null) _overlay.SetInfo(Marks.ToList(), _session.Reader.Target?.ProcessName ?? "client", TopPx, SidePx);
        LiveReadout.Clear();
        foreach (var m in Marks)
        {
            if (m.IsBar)
            {
                int pct = _ocr.ReadBarPercent(hwnd, m.X, m.Y, m.W, m.H, TopPx, SidePx);
                if (pct >= 0) LiveStats.Instance.SetNumber(m.Role, pct);
                LiveReadout.Add($"{m.Role} = {(pct < 0 ? "?" : pct + "%")}");
                continue;
            }
            if (Combined.TryGetValue(m.Role, out var pair))
            {
                string two = _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: true, topOffset: TopPx, sideOffset: SidePx);
                var parsed = OcrService.ParseTwoInts(two);
                if (parsed is { } pv) { LiveStats.Instance.SetNumber(pair.a, pv.Item1); LiveStats.Instance.SetNumber(pair.b, pv.Item2); }
                LiveReadout.Add($"{m.Role} = {(parsed is { } q ? $"{q.Item1} / {q.Item2}" : "?")}");
                continue;
            }
            string raw = _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: !m.IsText, topOffset: TopPx, sideOffset: SidePx);
            if (m.IsText)
            {
                string t = raw.Trim();
                if (t.Length > 0 && Stable(m.Role, t)) LiveStats.Instance.SetText(m.Role, t);
                LiveReadout.Add($"{m.Role} = {(t.Length == 0 ? "?" : t)}");
            }
            else
            {
                int n = OcrService.ParseFirstInt(raw);
                if (n >= 0 && Stable(m.Role, n.ToString())) LiveStats.Instance.SetNumber(m.Role, n);
                LiveReadout.Add($"{m.Role} = {(n < 0 ? "?" : n.ToString())}");
            }
        }
    }

    /// <summary>Commit a value only after it reads the same twice in a row — kills OCR flicker.</summary>
    private bool Stable(string role, string val)
    {
        if (_pending.TryGetValue(role, out var p) && p.val == val) _pending[role] = (val, p.count + 1);
        else _pending[role] = (val, 1);
        return _pending[role].count >= 2;
    }
}
