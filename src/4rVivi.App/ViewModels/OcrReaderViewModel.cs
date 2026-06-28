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
    private readonly Dictionary<string, byte[]> _markGray = new();   // last region signature for skip-unchanged
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
    public string[] PreprocessModes { get; } = { "Auto", "Light text", "Dark text", "Invert", "Grayscale", "High contrast", "Red", "Green", "Blue", "Cyan", "Yellow", "Magenta", "Saturation", "Max RGB", "Min RGB", "R-G", "R-B", "G-B", "Adaptive", "CLAHE", "Median", "Close" };
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
    [ObservableProperty] private bool _useDxgiCapture;   // guide §8 Stage 1: DXGI Desktop Duplication (monitor mode), GDI fallback
    private FourRVivi.App.Capture.DxgiDuplicationCapture? _dxgi;
    [ObservableProperty] private bool _runningCapture = true;   // continuous loop vs one screenshot
    public bool ScreenshotOnly { get => !RunningCapture; set => RunningCapture = !value; }
    partial void OnRunningCaptureChanged(bool value) => OnPropertyChanged(nameof(ScreenshotOnly));

    /// <summary>Capture either the whole selected monitor or the game window, per the chosen mode.</summary>
    private System.Drawing.Bitmap? CaptureFrame(IntPtr hwnd)
    {
        if (UseMonitor && SelectedMonitor != null)
        {
            if (UseDxgiCapture)
            {
                var dx = CaptureDxgi(SelectedMonitor.Index);
                if (dx != null) return dx;   // DXGI ok; else fall through to GDI
            }
            return _ocr.CaptureMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H);
        }
        return _ocr.CaptureWindow(hwnd);
    }

    /// <summary>Guide §8 Stage 1 — GPU capture via DXGI Desktop Duplication. Returns null on any failure
    /// or a no-change timeout so CaptureFrame can fall back to the GDI path.</summary>
    private System.Drawing.Bitmap? CaptureDxgi(int outputIndex)
    {
        try
        {
            _dxgi ??= new FourRVivi.App.Capture.DxgiDuplicationCapture();
            if (!_dxgi.IsInitialized && !_dxgi.TryInit(outputIndex)) return null;
            using var sk = _dxgi.Capture();
            return sk == null ? null : SkToBitmap(sk);
        }
        catch { return null; }
    }

    /// <summary>Copy a SkiaSharp Bgra8888 bitmap into a System.Drawing 32bpp ARGB bitmap (same byte order).</summary>
    private static System.Drawing.Bitmap SkToBitmap(SkiaSharp.SKBitmap sk)
    {
        var bmp = new System.Drawing.Bitmap(sk.Width, sk.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, sk.Width, sk.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            IntPtr src = sk.GetPixels();
            int srcStride = sk.RowBytes, dstStride = data.Stride, rowBytes = sk.Width * 4;
            var row = new byte[rowBytes];
            for (int y = 0; y < sk.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(src, y * srcStride), row, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * dstStride), rowBytes);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    /// <summary>For the calibration screenshot button (View grabs the selected monitor).</summary>
    public System.Drawing.Bitmap? GrabMonitor()
        => SelectedMonitor != null ? _ocr.CaptureMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H) : null;

    /// <summary>Grab the RO client window itself (client area) for the marking screenshot, so marks line
    /// up with what the OCR loop captures. Used when not in monitor mode.</summary>
    public System.Drawing.Bitmap? GrabWindow()
        => _session.WindowHandle != IntPtr.Zero ? _ocr.CaptureWindow(_session.WindowHandle) : null;

    // role keys the user can mark (CharName is text; the rest are numbers)
    public string[] Roles { get; } =
    {
        "BasicInfo",
        "HP / MaxHP", "SP / MaxSP", "Weight / MaxWeight",
        "BaseLevel", "JobLevel",
        "HP", "MaxHP", "SP", "MaxSP",
        "HpPercent", "SpPercent", "BaseExpBar", "JobExpBar", "CastBar",
        "Weight", "MaxWeight", "Zeny", "PosX", "PosY", "CharName", "ClassName",
        "MapName", "ItemName", "SkillName",
        "Ammo", "SkillBar", "BuffBar", "StatusIcons"
    };

    private static readonly Dictionary<string, (string a, string b)> Combined = new()
    {
        ["HP / MaxHP"] = ("HP", "MaxHP"),
        ["SP / MaxSP"] = ("SP", "MaxSP"),
        ["Weight / MaxWeight"] = ("Weight", "MaxWeight"),
    };

    public ObservableCollection<OcrMark> Marks { get; } = new();
    public ObservableCollection<ReadoutRow> Readout { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ReadoutRow> _rows = new();

    private enum LockState { Publish, HoldUser, Miss }
    // Publish a fresh read, hold the user-pinned value, or show a miss. (Auto-lock removed: fields are
    // made reliable by ground-truth Verify calibration instead.)
    private LockState Resolve(string role, bool ok)
    {
        var row = _rows.TryGetValue(role, out var r) ? r : null;
        if (row != null && row.UserLocked) return LockState.HoldUser;
        return ok ? LockState.Publish : LockState.Miss;
    }
    [ObservableProperty] private bool _detectText;       // full-frame text detection
    [ObservableProperty] private bool _detectMonsters;   // YOLO monster detection
    [ObservableProperty] private bool _detectSkills;     // skill/buff icon marks
    [ObservableProperty] private bool _detectMovement = true;   // character motion (stuck / finding monsters)
    [ObservableProperty] private bool _zoneScan = true;         // tile the frame into zones, detect each separately (internal, not drawn)
    [ObservableProperty] private bool _multiPass;               // try several preprocessings per read, keep best
    partial void OnMultiPassChanged(bool value) { _ocr.MultiPass = value; PersistToggles(); }
    [ObservableProperty] private bool _windowsForNumbers;       // route numeric fields through Windows OCR (great at clean digits)
    [ObservableProperty] private bool _ensemble;                // vote Paddle + Windows OCR per read
    [ObservableProperty] private bool _skipUnchanged = true;    // skip OCR when a region's pixels did not change
    private static bool GrayClose(byte[] a, byte[] b) { if (a.Length != b.Length) return false; long d = 0; long cap = a.Length * 4; for (int i = 0; i < a.Length; i++) { d += System.Math.Abs(a[i] - b[i]); if (d > cap) return false; } return true; }
    private bool _togglesLoaded;
    private void SyncOcrFromToggles()
    {
        _ocr.MultiPass = MultiPass;
        _ocr.EntityMinScore = (float)EntityMinScore;
        _ocr.TextMinScore = (float)TextMinScore;
        _ocr.NameEntitiesByText = GrfNamesAbove;
        _ocr.NameEntitiesByIcon = !GrfNamesAbove;
    }
    private void PersistToggles()
    {
        if (!_togglesLoaded) return;
        var t = _settings.Current.OcrToggles;
        t.DetectText = DetectText; t.DetectMonsters = DetectMonsters; t.DetectSkills = DetectSkills; t.DetectMovement = DetectMovement;
        t.ZoneScan = ZoneScan; t.ZoneCols = ZoneCols; t.ZoneRows = ZoneRows;
        t.MultiPass = MultiPass; t.WindowsForNumbers = WindowsForNumbers; t.Ensemble = Ensemble; t.SkipUnchanged = SkipUnchanged;
        t.GrfNamesAbove = GrfNamesAbove; t.IconCellPx = IconCellPx; t.OverlayValuesSize = OverlayValuesSize;
        t.EntityMinScore = EntityMinScore; t.TextMinScore = TextMinScore;
        _settings.Save();
    }
    partial void OnDetectTextChanged(bool value) => PersistToggles();
    partial void OnDetectMonstersChanged(bool value) => PersistToggles();
    partial void OnDetectSkillsChanged(bool value) => PersistToggles();
    partial void OnDetectMovementChanged(bool value) => PersistToggles();
    partial void OnZoneScanChanged(bool value) => PersistToggles();
    partial void OnZoneColsChanged(int value) => PersistToggles();
    partial void OnZoneRowsChanged(int value) => PersistToggles();
    partial void OnWindowsForNumbersChanged(bool value) => PersistToggles();
    partial void OnEnsembleChanged(bool value) => PersistToggles();
    partial void OnSkipUnchangedChanged(bool value) => PersistToggles();
    partial void OnIconCellPxChanged(int value) => PersistToggles();
    partial void OnOverlayValuesSizeChanged(double value) => PersistToggles();

    private string EngineFor(OcrMark m) => !string.IsNullOrEmpty(m.Engine) ? m.Engine : (Ensemble ? "Ensemble" : (WindowsForNumbers && !m.IsText ? "Windows" : "Paddle"));
    // guide §8 region pipelines: on "Auto" marks, let the role's RegionProfile pick the preprocess (monster/map->CLAHE, inventory->Adaptive).
    private string EffPre(OcrMark m) => string.IsNullOrEmpty(m.Preprocess) || m.Preprocess == "Auto" ? _ocr.SuggestPreprocess(m.Role) : m.Preprocess;
    [ObservableProperty] private int _zoneCols = 2;
    [ObservableProperty] private int _zoneRows = 2;
    [ObservableProperty] private int _iconCellPx = 32;   // skill/buff icon cell size for SkillBar/BuffBar marks
    [ObservableProperty] private bool _grfNamesAbove;   // GRF shows monster names above heads -> read text, else recognise sprite
    [ObservableProperty] private double _overlayValuesSize = 1.0;   // size of the on-client detected-values panel
    [ObservableProperty] private double _entityMinScore = 0.45;   // monster detection confidence floor
    [ObservableProperty] private double _textMinScore = 0.30;     // text/region confidence floor (RO tiny text scores low)
    [ObservableProperty] private bool _lockValues = true;   // hold the last confident value when a read fails
    [ObservableProperty] private int _exclX;   // HUD exclusion zone (capture px) ignored by detectors
    [ObservableProperty] private int _exclY;
    [ObservableProperty] private int _exclW;
    [ObservableProperty] private int _exclH;
    partial void OnEntityMinScoreChanged(double value) { _ocr.EntityMinScore = (float)value; PersistToggles(); }
    partial void OnTextMinScoreChanged(double value) { _ocr.TextMinScore = (float)value; PersistToggles(); }
    partial void OnExclXChanged(int value) => _ocr.ExclX = value;
    partial void OnExclYChanged(int value) => _ocr.ExclY = value;
    partial void OnExclWChanged(int value) => _ocr.ExclW = value;
    partial void OnExclHChanged(int value) => _ocr.ExclH = value;
    partial void OnGrfNamesAboveChanged(bool value) { _ocr.NameEntitiesByText = value; _ocr.NameEntitiesByIcon = !value; PersistToggles(); }
    [ObservableProperty] private string _selectedRole = "HP";
    partial void OnSelectedRoleChanged(string value) => OnPropertyChanged(nameof(SelectedRoleNote));
    public string SelectedRoleNote => RoleNotes.TryGetValue(SelectedRole ?? "", out var n) ? n
        : "Drag the box tightly over this value.";

    private static readonly System.Collections.Generic.Dictionary<string, string> RoleNotes = new()
    {
        ["BasicInfo"] = "TEXT block — drag over the whole basic-info panel (name/class/levels area).",
        ["HP / MaxHP"] = "NUMBER — drag over the \"current / max\" HP readout (e.g. 1234 / 5678).",
        ["SP / MaxSP"] = "NUMBER — drag over the \"current / max\" SP readout.",
        ["Weight / MaxWeight"] = "NUMBER — drag over the \"current / max\" weight readout.",
        ["BaseLevel"] = "NUMBER — drag over the base level digits only.",
        ["JobLevel"] = "NUMBER — drag over the job level digits only.",
        ["HP"] = "NUMBER — drag over the HP digits.",
        ["MaxHP"] = "NUMBER — drag over the Max HP digits.",
        ["SP"] = "NUMBER — drag over the SP digits.",
        ["MaxSP"] = "NUMBER — drag over the Max SP digits.",
        ["HpPercent"] = "BAR — drag over the colored HP bar fill (read as %). Not the numbers.",
        ["SpPercent"] = "BAR — drag over the colored SP bar fill (read as %).",
        ["BaseExpBar"] = "BAR — drag over the base EXP bar fill (read as %).",
        ["JobExpBar"] = "BAR — drag over the job EXP bar fill (read as %).",
        ["Character"] = "CHARACTER BOX — drag over your character sprite (reads movement / stuck).",
        ["Weight"] = "NUMBER — drag over the weight digits.",
        ["MaxWeight"] = "NUMBER — drag over the max weight digits.",
        ["Zeny"] = "NUMBER — drag over the zeny amount.",
        ["Loot"] = "TEXT — drag over the loot/pick-up message line.",
        ["PosX"] = "NUMBER — drag over the X coordinate readout.",
        ["PosY"] = "NUMBER — drag over the Y coordinate readout.",
        ["CharName"] = "TEXT — drag over your character name.",
        ["ClassName"] = "TEXT — drag over your class/job name.",
        ["MapName"] = "TEXT — drag over the current map name (top-right of screen).",
        ["Monster"] = "TEXT — drag over a monster's name label above its head.",
        ["Posture"] = "TEXT — drag over the posture/status word.",
        ["ItemName"] = "TEXT — drag over an item name.",
        ["SkillName"] = "TEXT — drag over a skill name.",
        ["Ammo"] = "NUMBER — drag over the ammo / rounds-left counter.",
        ["TargetName"] = "TEXT — drag over the target monster's name.",
        ["TargetHP"] = "NUMBER or BAR — drag over the target's HP readout.",
        ["PetName"] = "TEXT — drag over your pet/homun name.",
        ["SkillBar"] = "ICON STRIP — drag over the row of skill icons (each cell is recognized).",
        ["BuffBar"] = "ICON STRIP — drag over the row of buff icons (name + timer read per cell).",
        ["StatusIcons"] = "ICON STRIP — drag over the status-effect icon row.",
        ["CastBar"] = "BAR — drag over the cast/progress bar (read as %).",
    };
    [ObservableProperty] private bool _running;
    [ObservableProperty] private bool _calibrating;
    [ObservableProperty] private bool _calibrateUntilComplete;
    private volatile bool _calibCancel;
    private byte[]? _lastChar;
    [ObservableProperty] private string _engineInfo = "engine: -";
    [ObservableProperty] private string _status = "1) Load a screenshot. 2) Pick a stat, drag a box over it. 3) Save. 4) Start.";

    private readonly FourRVivi.Core.Game.OcrNameCorrector _corrector = new();
    private readonly FourRVivi.Core.Ocr.TemporalVotingService _vote = new();  // guide §8 Stage 9: last-N frame majority vote

    public OcrReaderViewModel(GameSession session, OcrService ocr, SettingsStore settings, DiscordPresenceUpdater discord,
                              Lazy<FourRVivi.Core.Data.GameDatabase> db)
    {
        _session = session; _ocr = ocr; _settings = settings; _discord = discord;
        // Build OCR correction dictionaries from our embedded data (snap fuzzy reads to real names).
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var d = db.Value;
                _corrector.SetDictionary("ClassName", FourRVivi.Core.Calc.ClassCatalog.All.Select(c => c.Name));
                _corrector.SetDictionary("Monster", d.AllMobs().Select(m => m.Name));
                _corrector.SetDictionary("MapName", d.SearchMaps("", 100000));
                _corrector.SetDictionary("ItemName", d.AllItems().Select(i => i.Name));
                _corrector.SetDictionary("SkillName", d.AllSkills().Select(s => s.Name));
            }
            catch { }
        });
        _ocr.Upscale = (int)System.Math.Round(_zoom);
        _ocr.Tuning = settings.Current.OcrTuning;
        _ocr.ApplyTuning();
        _detBoxThresh = settings.Current.OcrTuning.DetBoxThresh;
        _detUnclip = settings.Current.OcrTuning.DetUnclipRatio;
        _ocrCpuThreads = settings.Current.OcrTuning.CpuThreads;
        _readRotated = settings.Current.OcrTuning.DoAngle;
        _limitSide = settings.Current.OcrTuning.LimitSideLen;
        var tg = settings.Current.OcrToggles;
        _detectText = tg.DetectText; _detectMonsters = tg.DetectMonsters; _detectSkills = tg.DetectSkills; _detectMovement = tg.DetectMovement;
        _zoneScan = tg.ZoneScan; _zoneCols = tg.ZoneCols; _zoneRows = tg.ZoneRows;
        _multiPass = tg.MultiPass; _windowsForNumbers = tg.WindowsForNumbers; _ensemble = tg.Ensemble; _skipUnchanged = tg.SkipUnchanged;
        _grfNamesAbove = tg.GrfNamesAbove; _iconCellPx = tg.IconCellPx; _overlayValuesSize = tg.OverlayValuesSize;
        _entityMinScore = tg.EntityMinScore; _textMinScore = tg.TextMinScore;
        SyncOcrFromToggles();
        _togglesLoaded = true;
        foreach (var m in settings.Current.OcrMarks) Marks.Add(m);
        try { _hook = new GlobalKeyHook(); _hook.KeyPressed += OnGlobalKey; } catch { }
    }

    [ObservableProperty] private double _detBoxThresh;
    [ObservableProperty] private double _detUnclip;
    [ObservableProperty] private int _ocrCpuThreads;
    partial void OnDetBoxThreshChanged(double value) { _ocr.Tuning.DetBoxThresh = (float)value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnDetUnclipChanged(double value) { _ocr.Tuning.DetUnclipRatio = (float)value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnOcrCpuThreadsChanged(int value) { _ocr.Tuning.CpuThreads = value; _ocr.ApplyTuning(); _settings.Save(); }
    [ObservableProperty] private bool _readRotated;
    [ObservableProperty] private int _limitSide;
    partial void OnReadRotatedChanged(bool value) { _ocr.Tuning.DoAngle = value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnLimitSideChanged(int value) { _ocr.Tuning.LimitSideLen = Math.Max(64, value); _ocr.ApplyTuning(); _settings.Save(); }

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
        bool isBar = role is "BaseExpBar" or "JobExpBar" or "HpPercent" or "SpPercent" or "CastBar";
        bool isChar = role == "Character";
        bool isIcons = role is "SkillBar" or "BuffBar" or "StatusIcons";
        double minScore = role switch
        {
            "Monster" or "TargetName" => 0.40,
            "ItemName" or "SkillName" => 0.45,
            "CharName" or "ClassName" or "PetName" => 0.50,
            "MapName" => 0.55,
            _ => 0.0,   // numeric stats rely on the global floor
        };
        Marks.Add(new OcrMark { Role = role, X = x, Y = y, W = w, H = h, IsText = isText, IsBar = isBar, IsChar = isChar, IsIcons = isIcons, MinScore = minScore });
        Status = $"Marked {role}. Mark the rest, then Save.";
    }

    [RelayCommand] private void RemoveMark(OcrMark? m) { if (m != null) Marks.Remove(m); }
    [RelayCommand] private void ClearMarks() { Marks.Clear(); Status = "Cleared. Re-mark your stats."; }

    /// <summary>Combine markers that aim at the same thing: keep one mark per role, drop the rest.</summary>
    [RelayCommand] private void MergeMarks()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        var keep = new System.Collections.Generic.List<OcrMark>();
        int removed = 0;
        foreach (var m in Marks)
        {
            if (seen.Add(m.Role ?? "")) keep.Add(m); else removed++;
        }
        if (removed > 0)
        {
            Marks.Clear();
            foreach (var m in keep) Marks.Add(m);
            _settings.Current.OcrMarks = Marks.ToList(); _settings.Save();
        }
        Status = removed > 0 ? $"Merged {removed} duplicate marker(s) by role; {Marks.Count} left." : "No duplicate markers to merge.";
    }

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
        if (RunningCapture)
        {
            int period = Math.Max(1, IntervalMs);
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => BgTick(), null, 250, period);   // OFF the UI thread
            Status = $"Running capture every {period}ms (background) — keep the marked stats visible. Top bar, Stats and Discord now use it.";
        }
        else
        {
            // Screenshot-only: read the marks from a single frame, then stop (no loop).
            _timer?.Dispose(); _timer = null;
            await System.Threading.Tasks.Task.Run(() => BgTick());
            Running = false;
            Status = "Screenshot-only: read the marks once. Press Start again to re-read.";
        }
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
            // PER-MARK: each field gets its own winning (filter, sharpness, offset) so one global filter
            // can't starve the other fields. best[mark] = (mode, sharpen, dx, dy).
            var best = new Dictionary<OcrMark, (string mode, double sharp, double dx, double dy)>();
            double[] sharps = { 1.0, 2.0, 0.0 };
            long cap = Environment.TickCount64 + (CalibrateUntilComplete ? 180000 : 30000);
            int radius = 1;
            while (!_calibCancel && Environment.TickCount64 < cap)
            {
                var grid = BuildGrid(radius);
                using var frame = CaptureFrame(hwnd);
                if (frame == null) { System.Threading.Thread.Sleep(150); continue; }
                foreach (var m in fieldMarks)
                {
                    if (_calibCancel) break;
                    if (best.ContainsKey(m)) continue;
                    bool done = false;
                    foreach (var mode in PreprocessModes)
                    {
                        foreach (var sh in sharps)
                        {
                            foreach (var (dx, dy) in grid)
                                if (TryRead(frame, m, dx, dy, mode, sh)) { best[m] = (mode, sh, dx, dy); done = true; break; }
                            if (done) break;
                        }
                        if (done) break;
                    }
                    int cov = best.Count;
                    Dispatcher.UIThread.Post(() => Status = $"Per-mark tuning: {cov}/{fields} locked (radius {radius})…");
                }
                if (best.Count >= fields) break;
                if (!CalibrateUntilComplete) break;
                radius++;
            }

            Dispatcher.UIThread.Post(() =>
            {
                int moved = 0;
                foreach (var kv in best)
                {
                    var m = kv.Key; var (mode, sh, dx, dy) = kv.Value;
                    m.Preprocess = mode; m.Sharpen = sh;
                    if (dx != 0 || dy != 0) { m.X = Math.Clamp(m.X + dx, 0, 1); m.Y = Math.Clamp(m.Y + dy, 0, 1); moved++; }
                }
                _settings.Current.OcrMarks = Marks.ToList(); _settings.Save();
                int got = best.Count;
                var miss = fieldMarks.Where(m => !best.ContainsKey(m)).Select(m => m.Role);
                Calibrating = false;
                Status = got >= fields
                    ? $"Done — per-mark filter+sharpness locked for all {fields} fields; nudged {moved}. Saved. Press Start."
                    : $"Locked {got}/{fields} per-mark; nudged {moved}. Missing: {string.Join(", ", miss)}.";
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
        => TryRead(frame, m, dx, dy, _ocr.PreprocessMode, _ocr.Sharpen);

    private bool TryRead(System.Drawing.Bitmap frame, OcrMark m, double dx, double dy, string mode, double sharpen)
    {
        double fx = Math.Clamp(m.X + dx, 0, 1), fy = Math.Clamp(m.Y + dy, 0, 1);
        if (Combined.ContainsKey(m.Role)) return OcrService.ParseTwoInts(_ocr.ReadRectFrom(frame, fx, fy, m.W, m.H, true, EffTop, EffSide, mode, sharpen)) != null;
        if (m.IsText) return _ocr.ReadRectFrom(frame, fx, fy, m.W, m.H, false, EffTop, EffSide, mode, sharpen).Trim().Length >= 2;
        return OcrService.ParseFirstInt(_ocr.ReadRectFrom(frame, fx, fy, m.W, m.H, true, EffTop, EffSide, mode, sharpen)) >= 0;
    }

    [RelayCommand] private void ToggleOverlay()
    {
        OverlayOn = !OverlayOn;
        if (OverlayOn) ShowOverlay(); else HideOverlay();
    }

    [RelayCommand] private async Task VerifyCalibrate()
    {
        if (Calibrating) { _calibCancel = true; Status = "Stopping calibration…"; return; }
        if (UseMonitor && SelectedMonitor == null) { Status = "Pick a monitor first."; return; }
        if (!UseMonitor && _session.WindowHandle == IntPtr.Zero) { Status = "Attach your RO process (or use Monitor capture)."; return; }
        if (!await _ocr.EnsureDataAsync()) { Status = "Couldn't get OCR data (no internet?)."; return; }

        var targets = new System.Collections.Generic.List<(OcrMark m, string exp, bool numeric, bool combined)>();
        foreach (var row in Readout)
        {
            if (string.IsNullOrWhiteSpace(row.Expected)) continue;
            var m = Marks.FirstOrDefault(x => x.Role == row.Role);
            if (m == null || m.IsBar || m.IsChar || m.IsIcons) continue;
            m.Expected = row.Expected.Trim();
            bool combined = Combined.ContainsKey(m.Role);
            targets.Add((m, m.Expected, combined || !m.IsText, combined));
        }
        if (targets.Count == 0) { Status = "Type the correct value in the Expected column for the fields you want to calibrate, then press Verify."; return; }

        Calibrating = true; _calibCancel = false;
        IntPtr hwnd = _session.WindowHandle;
        await Task.Run(() =>
        {
            int matched = 0, done = 0;
            foreach (var (m, exp, numeric, combined) in targets)
            {
                if (_calibCancel) break;
                using var frame = CaptureFrame(hwnd);
                if (frame == null) { System.Threading.Thread.Sleep(120); continue; }
                var (mode, sharpen, dx, dy, ok) = _ocr.CalibrateToValue(frame, m.X, m.Y, m.W, m.H, exp, numeric, combined, EffTop, EffSide);
                m.Preprocess = mode; m.Sharpen = sharpen;
                m.X = Math.Clamp(m.X + dx, 0, 1); m.Y = Math.Clamp(m.Y + dy, 0, 1);
                m.Calibrated = ok; if (ok) matched++; done++;
                int mm = matched, dd = done, tt = targets.Count;
                Dispatcher.UIThread.Post(() => Status = $"Calibrating to your values… {dd}/{tt} ({mm} matched).");
            }
            Dispatcher.UIThread.Post(() =>
            {
                _activeMarks = Marks.ToList();
                _settings.Current.OcrMarks = Marks.ToList(); _settings.Save();
                Calibrating = false;
                Status = $"Verify done: {matched}/{targets.Count} fields now read your exact value, each locked to its own colour/sharpness/offset.";
            });
        });
    }

    private void ShowOverlay()
    {
        try
        {
            _overlay ??= new OcrOverlayWindow(_session);
            if (UseMonitor && SelectedMonitor != null)
                _overlay.SetMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H);
            else
                _overlay.ClearMonitor();
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
            var rowData = new System.Collections.Generic.List<(string role, string val, string conf)>();
            foreach (var m in _activeMarks)
            {
                if (m.IsChar)
                {
                    if (!DetectMovement) continue;
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
                    rowData.Add(("Character", motion.ToString(), ""));
                    continue;
                }
                if (m.IsBar)
                {
                    int pct = frame != null ? _ocr.ReadBarPercentFrom(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide) : _ocr.ReadBarPercent(hwnd, m.X, m.Y, m.W, m.H, TopPx, SidePx);
                    var stB = Resolve(m.Role, pct >= 0);
                    if (stB == LockState.Publish) { LiveStats.Instance.SetNumber(m.Role, pct); rowData.Add((m.Role, pct + "%", "")); }
                    else if (stB != LockState.Miss && LiveStats.Instance.TryGetNumber(m.Role, out var lp)) { LiveStats.Instance.SetNumber(m.Role, lp); rowData.Add((m.Role, lp + "%", "user")); }
                    else rowData.Add((m.Role, "?", ""));
                    continue;
                }
                if (SkipUnchanged && frame != null)
                {
                    var gnow = _ocr.CropGray(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide);
                    if (gnow != null)
                    {
                        if (_markGray.TryGetValue(m.Role, out var gprev) && GrayClose(gprev, gnow))
                        {
                            if (Combined.TryGetValue(m.Role, out var pc) && LiveStats.Instance.TryGetNumber(pc.a, out var cva) && LiveStats.Instance.TryGetNumber(pc.b, out var cvb))
                            { LiveStats.Instance.SetNumber(pc.a, cva); LiveStats.Instance.SetNumber(pc.b, cvb); rowData.Add((m.Role, $"{cva} / {cvb}", "cache")); continue; }
                            if (m.IsText) { var lt = LiveStats.Instance.GetText(m.Role); if (!string.IsNullOrEmpty(lt)) { LiveStats.Instance.SetText(m.Role, lt); rowData.Add((m.Role, lt, "cache")); continue; } }
                            else if (LiveStats.Instance.TryGetNumber(m.Role, out var cln)) { LiveStats.Instance.SetNumber(m.Role, cln); rowData.Add((m.Role, cln.ToString(), "cache")); continue; }
                        }
                        _markGray[m.Role] = gnow;
                    }
                }
                if (Combined.TryGetValue(m.Role, out var pair))
                {
                    string two = frame != null ? (MultiPass ? _ocr.ReadRectBest(frame, m.X, m.Y, m.W, m.H, true, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m)) : _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, true, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m))) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: true, topOffset: TopPx, sideOffset: SidePx);
                    double confC = _ocr.LastRecScore;
                    var parsed = OcrService.ParseTwoInts(two);
                    var stC = Resolve(m.Role, parsed is { } && (m.MinScore <= 0 || confC >= m.MinScore));
                    if (stC == LockState.Publish && parsed is { } pv) { LiveStats.Instance.SetNumber(pair.a, pv.Item1); LiveStats.Instance.SetNumber(pair.b, pv.Item2); rowData.Add((m.Role, $"{pv.Item1} / {pv.Item2}", "")); }
                    else if (stC != LockState.Miss && LiveStats.Instance.TryGetNumber(pair.a, out var la) && LiveStats.Instance.TryGetNumber(pair.b, out var lb))
                    { LiveStats.Instance.SetNumber(pair.a, la); LiveStats.Instance.SetNumber(pair.b, lb); rowData.Add((m.Role, $"{la} / {lb}", "user")); }
                    else rowData.Add((m.Role, "?", ""));
                    continue;
                }
                string raw = frame != null ? (MultiPass ? _ocr.ReadRectBest(frame, m.X, m.Y, m.W, m.H, !m.IsText, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m)) : _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, !m.IsText, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m))) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: !m.IsText, topOffset: TopPx, sideOffset: SidePx);
                double conf = _ocr.LastRecScore;
                if (m.IsText)
                {
                    string t = raw.Trim();
                    // Snap to a real game name when we have a dictionary for this role (class/map/monster/item/skill).
                    if (t.Length > 0 && _corrector.HasRole(m.Role)) t = _corrector.Correct(m.Role, t);
                    if (t.Length > 0) t = _vote.Vote(m.Role, t);   // temporal voting: one bad frame can't flip the overlay
                    var stT = Resolve(m.Role, t.Length > 0 && (m.MinScore <= 0 || conf >= m.MinScore));
                    if (stT == LockState.Publish)
                    {
                        if (Stable(m.Role, t, conf)) LiveStats.Instance.SetText(m.Role, t);
                        rowData.Add((m.Role, t, conf.ToString("0.00")));
                    }
                    else if (stT != LockState.Miss)
                    {
                        var last = LiveStats.Instance.GetText(m.Role);
                        if (!string.IsNullOrEmpty(last)) { LiveStats.Instance.SetText(m.Role, last); rowData.Add((m.Role, last, "user")); }
                        else rowData.Add((m.Role, "?", ""));
                    }
                    else rowData.Add((m.Role, "?", ""));
                }
                else
                {
                    int n = OcrService.ParseFirstInt(raw);
                    var stN = Resolve(m.Role, n >= 0 && (m.MinScore <= 0 || conf >= m.MinScore));
                    if (stN == LockState.Publish)
                    {
                        if (Stable(m.Role, n.ToString(), conf)) LiveStats.Instance.SetNumber(m.Role, n);
                        rowData.Add((m.Role, n.ToString(), conf.ToString("0.00")));
                    }
                    else if (stN != LockState.Miss && LiveStats.Instance.TryGetNumber(m.Role, out var lastN))
                    { LiveStats.Instance.SetNumber(m.Role, lastN); rowData.Add((m.Role, lastN.ToString(), "user")); }
                    else rowData.Add((m.Role, "?", ""));
                }
            }

            var dets = new List<FourRVivi.App.Overlay.DetBox>();
            int capW = frame?.Width ?? 0, capH = frame?.Height ?? 0;

            // SKILL/BUFF ICON MARKS: recognise each icon cell of a SkillBar/BuffBar/StatusIcons region.
            var buffStatuses = new List<string>();
            if (DetectSkills && frame != null)
            {
                foreach (var m in _activeMarks)
                {
                    if (!m.IsIcons) continue;
                    bool isBuff = m.Role is "BuffBar" or "StatusIcons";
                    var names = new List<string>();
                    try
                    {
                        foreach (var hit in _ocr.ScanIcons(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide, IconCellPx, isBuff))
                        {
                            string lbl = hit.Timer >= 0 ? $"{hit.Label} {hit.Timer}s" : hit.Label;
                            names.Add(lbl);
                            dets.Add(new FourRVivi.App.Overlay.DetBox("Entity", hit.X, hit.Y, hit.W, hit.H, lbl, hit.Score));
                            if (isBuff) buffStatuses.Add(hit.Label);
                        }
                    }
                    catch { }
                    if (names.Count > 0) rowData.Add((m.Role, string.Join(", ", names), ""));
                }
            }

            // AUTO-DETECTION: read the whole frame (all text + all entities) and publish to LiveScene so
            // the Smart Bot (target real monsters) and Auto Debuff (cure on status text) engines can act.
            // Entity coords equal the window client area only when capturing the window, not a monitor.
            if ((DetectText || DetectMonsters) && frame != null)
            {
                _ocr.ScanTextEnabled = DetectText; _ocr.ScanEntitiesEnabled = DetectMonsters;
                try
                {
                    var ents = new List<FourRVivi.Core.Game.SceneItem>();
                    var texts = new List<string>();
                    var scan = ZoneScan ? _ocr.ScanScreenZoned(frame, ZoneCols, ZoneRows) : _ocr.ScanScreen(frame);
                    foreach (var fnd in scan)
                    {
                        if (fnd.Kind == "Entity")
                            ents.Add(new FourRVivi.Core.Game.SceneItem(fnd.X, fnd.Y, fnd.W, fnd.H, fnd.Value, fnd.Score));
                        else if (!string.IsNullOrWhiteSpace(fnd.Value))
                            texts.Add(fnd.Value);
                        dets.Add(new FourRVivi.App.Overlay.DetBox(fnd.Kind, fnd.X, fnd.Y, fnd.W, fnd.H, fnd.Value, fnd.Score));
                    }
                    texts.AddRange(buffStatuses);
                    FourRVivi.Core.Game.LiveScene.Instance.Active = true;
                    FourRVivi.Core.Game.LiveScene.Instance.SetEntities(ents, clientCoords: !UseMonitor);
                    FourRVivi.Core.Game.LiveScene.Instance.SetStatuses(texts);
                }
                catch { }
            }
            else if (buffStatuses.Count > 0)
            {
                FourRVivi.Core.Game.LiveScene.Instance.Active = true;
                FourRVivi.Core.Game.LiveScene.Instance.SetStatuses(buffStatuses);
            }

            string eng = _ocr.LastEngine;
            string warn = _ocr.EngineWarning;
            Dispatcher.UIThread.Post(() =>
            {
                EngineInfo = "engine: " + eng + (string.IsNullOrEmpty(warn) ? "" : "  \u26A0 " + warn);
                if (!string.IsNullOrEmpty(warn)) Status = "\u26A0 " + warn;
                foreach (var rd in rowData)
                {
                    if (_rows.TryGetValue(rd.role, out var row)) { row.Value = rd.val; row.Conf = rd.conf; }
                    else { var nr = new ReadoutRow { Role = rd.role, Value = rd.val, Conf = rd.conf, Expected = Marks.FirstOrDefault(x => x.Role == rd.role)?.Expected ?? "" }; _rows[rd.role] = nr; Readout.Add(nr); }
                }
                if (_overlay != null)
                {
                    _overlay.SetInfo(Marks.ToList(), _session.Reader.Target?.ProcessName ?? "client", TopPx, SidePx);
                    _overlay.SetDetections(dets, capW, capH);
                    _overlay.SetValues(rowData.Select(r => r.role + " = " + r.val).ToList(), OverlayValuesSize);
                }
            });
        }
        catch { }
        finally { _busy = false; }
    }

    /// <summary>Commit a value only after it reads the same twice in a row — kills OCR flicker.</summary>
    private bool Stable(string role, string val, double conf = 0)
    {
        if (conf >= 0.92) { _pending[role] = (val, 2); return true; }   // trust a very confident single read
        if (_pending.TryGetValue(role, out var p) && p.val == val) _pending[role] = (val, p.count + 1);
        else _pending[role] = (val, 1);
        return _pending[role].count >= 2;
    }
}

public sealed partial class ReadoutRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _role = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _value = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _conf = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _expected = "";   // user-typed ground truth for Verify calibration
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _userLocked;   // user pins the value (OCR got it wrong)
}
