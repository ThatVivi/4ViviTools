using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
    [ObservableProperty] private double _zoom = 4.0;
    public string[] PreprocessModes { get; } = { "Auto", "Light text", "Dark text", "Invert", "Grayscale", "High contrast", "Red", "Green", "Blue" };
    [ObservableProperty] private string _preprocessMode = "Auto";
    partial void OnPreprocessModeChanged(string value) => _ocr.PreprocessMode = string.IsNullOrEmpty(value) ? "Auto" : value;
    [ObservableProperty] private double _sharpness = 1.0;
    partial void OnSharpnessChanged(double value) => _ocr.Sharpen = value;
    partial void OnZoomChanged(double value) => _ocr.Upscale = (int)System.Math.Round(value);

    private int TopPx => WindowMode == "Windowed" ? TopOffset : 0;
    private int SidePx => WindowMode == "Windowed" ? SideOffset : 0;
    private int EffTop => UseMonitor ? 0 : TopPx;     // monitor capture = no title-bar offset
    private int EffSide => UseMonitor ? 0 : SidePx;

    public System.Collections.ObjectModel.ObservableCollection<MonitorInfo> Monitors { get; } = new();
    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private bool _useMonitor;

    /// <summary>Capture either the whole selected monitor or the game window, per the chosen mode.</summary>
    private System.Drawing.Bitmap? CaptureFrame(IntPtr hwnd)
        => (UseMonitor && SelectedMonitor != null)
            ? _ocr.CaptureMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H)
            : _ocr.CaptureWindow(hwnd);

    /// <summary>For the calibration screenshot button (View grabs the selected monitor).</summary>
    public System.Drawing.Bitmap? GrabMonitor()
        => SelectedMonitor != null ? _ocr.CaptureMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H) : null;

    // role keys the user can mark (CharName is text; the rest are numbers)
    public string[] Roles { get; } =
    {
        "BasicInfo",
        "HP / MaxHP", "SP / MaxSP", "Weight / MaxWeight",
        "BaseLevel", "JobLevel",
        "HP", "MaxHP", "SP", "MaxSP",
        "HpPercent", "SpPercent", "BaseExpBar", "JobExpBar", "Character",
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
    [ObservableProperty] private bool _calibrating;
    [ObservableProperty] private bool _calibrateUntilComplete;
    private volatile bool _calibCancel;
    private byte[]? _lastChar;
    [ObservableProperty] private string _engineInfo = "engine: -";
    [ObservableProperty] private string _status = "1) Load a screenshot. 2) Pick a stat, drag a box over it. 3) Save. 4) Start.";

    public OcrReaderViewModel(GameSession session, OcrService ocr, SettingsStore settings, DiscordPresenceUpdater discord)
    {
        _session = session; _ocr = ocr; _settings = settings; _discord = discord;
        _ocr.Upscale = (int)System.Math.Round(_zoom);
        _ocr.Tuning = settings.Current.OcrTuning;
        _ocr.ApplyTuning();
        _detBoxThresh = settings.Current.OcrTuning.DetBoxThresh;
        _detUnclip = settings.Current.OcrTuning.DetUnclipRatio;
        _ocrCpuThreads = settings.Current.OcrTuning.CpuThreads;
        foreach (var m in settings.Current.OcrMarks) Marks.Add(m);
        try { _hook = new GlobalKeyHook(); _hook.KeyPressed += OnGlobalKey; } catch { }
    }

    [ObservableProperty] private double _detBoxThresh;
    [ObservableProperty] private double _detUnclip;
    [ObservableProperty] private int _ocrCpuThreads;
    partial void OnDetBoxThreshChanged(double v) { _ocr.Tuning.DetBoxThresh = (float)v; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnDetUnclipChanged(double v) { _ocr.Tuning.DetUnclipRatio = (float)v; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnOcrCpuThreadsChanged(int v) { _ocr.Tuning.CpuThreads = v; _ocr.ApplyTuning(); _settings.Save(); }

    private readonly OcrTrainerRunner _trainer = new();
    private CancellationTokenSource? _trainCts;
    [ObservableProperty] private string _trainLog = "";
    [ObservableProperty] private bool _training;

    [RelayCommand]
    private void ExportTemplate()
    {
        byte[]? png = null;
        try { var f = GrabMonitor(); if (f != null) png = OcrService.BitmapToPng(f); } catch { }
        var dir = OcrTemplateExporter.Export(Marks, png);
        Status = $"Exported template + reference image to {dir}.";
    }

    [RelayCommand]
    private async Task TrainOcr()
    {
        var dir = _trainer.UserImagesDir();
        try { Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true }); } catch { }
        Status = $"Drop ~30-40 screenshots into {dir}, then training runs.";
        TrainLog = "";
        _trainer.Line -= OnTrainLine; _trainer.Line += OnTrainLine;
        _trainCts = new CancellationTokenSource();
        Training = true;
        var ok = await _trainer.RunAsync(_trainCts.Token);
        Training = false;
        Status = ok ? "Training done — new model installed. Reloading OCR…" : "Training cancelled or failed (old model kept).";
        if (ok) _ocr.ReloadWorker();
    }

    private void OnTrainLine(string l) => Dispatcher.UIThread.Post(() => TrainLog += l + "\n");

    [RelayCommand] private void CancelTrain() { _trainCts?.Cancel(); }

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
        bool isBar = role is "BaseExpBar" or "JobExpBar" or "HpPercent" or "SpPercent";
        bool isChar = role == "Character";
        Marks.Add(new OcrMark { Role = role, X = x, Y = y, W = w, H = h, IsText = isText, IsBar = isBar, IsChar = isChar });
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
        if (UseMonitor && SelectedMonitor == null) { Status = "Pick a monitor first."; return; }
        if (!UseMonitor && _session.WindowHandle == IntPtr.Zero) { Status = "Pick your RO process (or switch to Monitor capture)."; return; }
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

    /// <summary>Cycle through EVERY filter (and grow the search radius) nudging boxes, recording which
    /// filter+offset reads each field; then lock in the filter that covers the most. Press again to stop.</summary>
    [RelayCommand] private async Task Calibrate()
    {
        if (Calibrating) { _calibCancel = true; Status = "Stopping calibration…"; return; }
        if (Marks.Count == 0) { Status = "Mark your stats first."; return; }
        if (UseMonitor && SelectedMonitor == null) { Status = "Pick a monitor first."; return; }
        if (!UseMonitor && _session.WindowHandle == IntPtr.Zero) { Status = "Attach your RO process (or use Monitor capture)."; return; }
        if (Running) { Status = "Stop OCR before calibrating."; return; }
        if (!await _ocr.EnsureDataAsync()) { Status = "Couldn't get OCR data (no internet?)."; return; }

        Calibrating = true; _calibCancel = false;
        var marks = Marks.ToList();
        var fieldMarks = marks.Where(m => !m.IsBar).ToList();
        int fields = fieldMarks.Count;
        IntPtr hwnd = _session.WindowHandle;

        await Task.Run(() =>
        {
            // found[filter][mark] = winning offset
            var found = new Dictionary<string, Dictionary<OcrMark, (double dx, double dy)>>();
            foreach (var mode in PreprocessModes) found[mode] = new Dictionary<OcrMark, (double, double)>();

            long cap = Environment.TickCount64 + (CalibrateUntilComplete ? 180000 : 30000);
            int radius = 1;
            while (!_calibCancel && Environment.TickCount64 < cap)
            {
                var grid = BuildGrid(radius);
                foreach (var mode in PreprocessModes)
                {
                    if (_calibCancel) break;
                    _ocr.PreprocessMode = mode;
                    Dispatcher.UIThread.Post(() => { PreprocessMode = mode; Status = $"Trying filter \u201c{mode}\u201d (radius {radius})…"; });
                    using var frame = CaptureFrame(hwnd);
                    if (frame == null) { System.Threading.Thread.Sleep(120); continue; }
                    foreach (var m in fieldMarks)
                    {
                        if (found[mode].ContainsKey(m)) continue;
                        foreach (var (dx, dy) in grid)
                            if (TryRead(frame, m, dx, dy)) { found[mode][m] = (dx, dy); break; }
                    }
                    int cov = found[mode].Count;
                    Dispatcher.UIThread.Post(() => Status = $"Filter \u201c{mode}\u201d: {cov}/{fields} (radius {radius})");
                    System.Threading.Thread.Sleep(60);
                }
                if (found.Values.Any(d => d.Count >= fields)) break;   // a filter covers everything
                if (!CalibrateUntilComplete) break;
                radius++;
            }

            string bestMode = found.OrderByDescending(kv => kv.Value.Count).First().Key;
            Dispatcher.UIThread.Post(() =>
            {
                _ocr.PreprocessMode = bestMode; PreprocessMode = bestMode;
                int moved = 0;
                foreach (var kv in found[bestMode])
                {
                    var (dx, dy) = kv.Value;
                    if (dx != 0 || dy != 0) { kv.Key.X = Math.Clamp(kv.Key.X + dx, 0, 1); kv.Key.Y = Math.Clamp(kv.Key.Y + dy, 0, 1); moved++; }
                }
                _settings.Current.OcrMarks = Marks.ToList(); _settings.Save();
                int got = found[bestMode].Count;
                var miss = fieldMarks.Where(m => !found[bestMode].ContainsKey(m)).Select(m => m.Role);
                Calibrating = false;
                Status = got >= fields
                    ? $"Done — filter \u201c{bestMode}\u201d read all {fields} fields; nudged {moved}. Saved. Press Start."
                    : $"Best filter \u201c{bestMode}\u201d read {got}/{fields}; nudged {moved}. Missing: {string.Join(", ", miss)}.";
            });
        });
    }

    private static System.Collections.Generic.List<(double dx, double dy)> BuildGrid(int radius)
    {
        var g = new System.Collections.Generic.List<(double, double)>();
        const double step = 0.006;
        for (int sy = -radius; sy <= radius; sy++)
            for (int sx = -radius; sx <= radius; sx++)
                g.Add((Math.Clamp(sx * step, -0.15, 0.15), Math.Clamp(sy * step, -0.15, 0.15)));
        return g;
    }

    private bool TryRead(System.Drawing.Bitmap frame, OcrMark m, double dx, double dy)
    {
        double fx = Math.Clamp(m.X + dx, 0, 1), fy = Math.Clamp(m.Y + dy, 0, 1);
        if (Combined.ContainsKey(m.Role)) return OcrService.ParseTwoInts(_ocr.ReadRectFrom(frame, fx, fy, m.W, m.H, true, EffTop, EffSide)) != null;
        if (m.IsText) return _ocr.ReadRectFrom(frame, fx, fy, m.W, m.H, false, EffTop, EffSide).Trim().Length >= 2;
        return OcrService.ParseFirstInt(_ocr.ReadRectFrom(frame, fx, fy, m.W, m.H, true, EffTop, EffSide)) >= 0;
    }

    [RelayCommand] private void ToggleOverlay()
    {
        OverlayOn = !OverlayOn;
        if (OverlayOn) ShowOverlay(); else HideOverlay();
    }

    private void ShowOverlay()
    {
        if (UseMonitor) { Status = "Overlay disabled in Monitor mode (it would be captured). Boxes show on the calibration image."; return; }
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
            using var frame = CaptureFrame(hwnd);   // monitor or window
            var readout = new List<string>();
            foreach (var m in _activeMarks)
            {
                if (m.IsChar)
                {
                    var g = frame != null ? _ocr.CropGray(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide) : null;
                    int motion = 0;
                    if (g != null)
                    {
                        if (_lastChar != null && _lastChar.Length == g.Length)
                        {
                            long d = 0; for (int i = 0; i < g.Length; i++) d += Math.Abs(g[i] - _lastChar[i]);
                            motion = (int)Math.Clamp((d / (double)g.Length) * 2.5, 0, 100);
                        }
                        _lastChar = g;
                        LiveStats.Instance.SetNumber("CharMotion", motion);
                    }
                    readout.Add($"Character motion = {motion}");
                    continue;
                }
                if (m.IsBar)
                {
                    int pct = frame != null ? _ocr.ReadBarPercentFrom(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide) : _ocr.ReadBarPercent(hwnd, m.X, m.Y, m.W, m.H, TopPx, SidePx);
                    if (pct >= 0) LiveStats.Instance.SetNumber(m.Role, pct);
                    readout.Add($"{m.Role} = {(pct < 0 ? "?" : pct + "%")}");
                    continue;
                }
                if (Combined.TryGetValue(m.Role, out var pair))
                {
                    string two = frame != null ? _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, true, EffTop, EffSide) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: true, topOffset: TopPx, sideOffset: SidePx);
                    var parsed = OcrService.ParseTwoInts(two);
                    if (parsed is { } pv) { LiveStats.Instance.SetNumber(pair.a, pv.Item1); LiveStats.Instance.SetNumber(pair.b, pv.Item2); }
                    readout.Add($"{m.Role} = {(parsed is { } q ? $"{q.Item1} / {q.Item2}" : "? [" + two.Trim() + "]")}");
                    continue;
                }
                string raw = frame != null ? _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, !m.IsText, EffTop, EffSide) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: !m.IsText, topOffset: TopPx, sideOffset: SidePx);
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

            string eng = _ocr.LastEngine;
            Dispatcher.UIThread.Post(() =>
            {
                EngineInfo = "engine: " + eng;
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
