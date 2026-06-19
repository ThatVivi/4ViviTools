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

    // role keys the user can mark (CharName is text; the rest are numbers)
    public string[] Roles { get; } =
    {
        "HP", "MaxHP", "SP", "MaxSP", "BaseLevel", "JobLevel",
        "Weight", "MaxWeight", "Zeny", "BaseEXP", "JobEXP", "CharName"
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
        Marks.Add(new OcrMark { Role = role, X = x, Y = y, W = w, H = h, IsText = role == "CharName" });
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
        try
        {
            _overlay = new OcrOverlayWindow(_session);
            _overlay.SetInfo(Marks.ToList(), _session.Reader.Target?.ProcessName ?? "client");
            _overlay.Show();
        }
        catch { _overlay = null; }
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
        try { _overlay?.Close(); } catch { }
        _overlay = null;
        Status = "OCR stopped. Back to memory mode.";
    }

    private void Tick()
    {
        var hwnd = _session.WindowHandle;
        if (hwnd == IntPtr.Zero) { Status = "Lost the game window — is it still open?"; return; }
        LiveReadout.Clear();
        foreach (var m in Marks)
        {
            string raw = _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: !m.IsText);
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
