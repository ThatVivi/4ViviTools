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
using FourRVivi.Core.Automation;
using FourRVivi.Core.Common;
using FourRVivi.App.Services;
using FourRVivi.App.Overlay;

namespace FourRVivi.App.ViewModels;

/// <summary>OCR reader: calibrate once by marking a screenshot, then read those regions off the live
/// window every tick and feed LiveStats (top bar, Stats tab, Discord). No memory access.</summary>
public sealed partial class OcrReaderViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly OcrService _ocr;
    private readonly SettingsStore _settings;
    private readonly EngineHub _hub;
    private System.Threading.Timer? _timer;
    private volatile bool _busy;
    private List<OcrMark> _activeMarks = new();
    private readonly DiscordPresenceUpdater _discord;
    private GlobalKeyHook? _hook;
    private OcrOverlayWindow? _overlay;
    private readonly Dictionary<string, (string val, int count)> _pending = new();
    private readonly Dictionary<string, (int value, int count)> _percentPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<int>> _barSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _markGray = new();   // last region signature for skip-unchanged
    private long _ocrTick;
    private long _lastOverlayPushMs;
    private long _lastEntityDiagMs;
    private long _lastRuntimeRefreshMs;
    private long _lastFocusGateMs;
    private double _entityScanEmaMs;
    private double _textScanEmaMs;
    private double _tickEmaMs;
    [ObservableProperty] private int _intervalMs = -1;
    public string[] WindowModes { get; } = { "Fullscreen", "Windowed" };
    [ObservableProperty] private string _windowMode = "Windowed";
    [ObservableProperty] private int _topOffset = 31;   // title bar height (windowed)
    [ObservableProperty] private int _sideOffset = 8;   // window border (windowed)
    [ObservableProperty] private bool _overlayOn;
    [ObservableProperty] private string _overlayHotkey = "F8";
    [ObservableProperty] private string _ocrHotkey = "F9";
    [ObservableProperty] private bool _showAdvanced;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
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
    private bool BotForcesClientCapture => _hub.SmartBot.Enabled;
    private int EffTop => UseMonitor && !BotForcesClientCapture ? 0 : TopPx;     // monitor capture = no title-bar offset
    private int EffSide => UseMonitor && !BotForcesClientCapture ? 0 : SidePx;

    public System.Collections.ObjectModel.ObservableCollection<MonitorInfo> Monitors { get; } = new();
    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private bool _useMonitor;
    [ObservableProperty] private bool _useDxgiCapture = true;   // GPU Desktop Duplication in monitor mode, GDI fallback.
    private FourRVivi.App.Capture.DxgiDuplicationCapture? _dxgi;
    [ObservableProperty] private bool _runningCapture = true;   // continuous loop vs one screenshot
    public bool ScreenshotOnly { get => !RunningCapture; set => RunningCapture = !value; }
    partial void OnRunningCaptureChanged(bool value) => OnPropertyChanged(nameof(ScreenshotOnly));

    /// <summary>Capture either the whole selected monitor or the game window, per the chosen mode.</summary>
    private System.Drawing.Bitmap? CaptureFrame(IntPtr hwnd)
    {
        if (UseMonitor && SelectedMonitor != null && !BotForcesClientCapture)
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

    /// <summary>GPU capture via DXGI Desktop Duplication. Returns null on any failure
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
    private static unsafe System.Drawing.Bitmap SkToBitmap(SkiaSharp.SKBitmap sk)
    {
        var bmp = new System.Drawing.Bitmap(sk.Width, sk.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, sk.Width, sk.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            byte* src = (byte*)sk.GetPixels();
            byte* dst = (byte*)data.Scan0;
            int srcStride = sk.RowBytes, dstStride = data.Stride, rowBytes = sk.Width * 4;
            for (int y = 0; y < sk.Height; y++)
            {
                int dstOffset = dstStride < 0 ? (sk.Height - 1 - y) * -dstStride : y * dstStride;
                Buffer.MemoryCopy(
                    source: src + (y * srcStride),
                    destination: dst + dstOffset,
                    destinationSizeInBytes: rowBytes,
                    sourceBytesToCopy: rowBytes);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    /// <summary>For the calibration screenshot button (View grabs the selected monitor).</summary>
    public System.Drawing.Bitmap? GrabMonitor()
    {
        if (SelectedMonitor == null) return null;
        if (UseDxgiCapture)
        {
            var dx = CaptureDxgi(SelectedMonitor.Index);
            if (dx != null) return dx;
        }
        return _ocr.CaptureMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H);
    }

    /// <summary>Grab the RO client window itself (client area) for the marking screenshot, so marks line
    /// up with what the OCR loop captures. Used when not in monitor mode.</summary>
    public System.Drawing.Bitmap? GrabWindow()
        => _session.WindowHandle != IntPtr.Zero ? _ocr.CaptureWindow(_session.WindowHandle) : null;

    // role keys the user can mark. Name-like roles are text; stat roles are numeric/bars/icons.
    public string[] Roles { get; } =
    {
        "BasicInfo",
        "HP % Text", "SP % Text", "Weight / MaxWeight",
        "BaseLevel", "JobLevel",
        "BaseExpBar", "JobExpBar", "CastBar",
        "Weight", "MaxWeight", "Zeny", "PosX", "PosY", "CharName", "ClassName",
        "MapName", "Monster", "TargetName", "TargetHP", "PetName", "Loot", "Posture", "ItemName", "SkillName",
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
    [ObservableProperty] private bool _fastMonsterTracking = true;
    [ObservableProperty] private int _textScanEvery = -1;
    [ObservableProperty] private int _overlayFrameMs = -1;
    [ObservableProperty] private int _maxOverlayDetections = 40;
    private static bool GrayClose(byte[] a, byte[] b) { if (a.Length != b.Length) return false; long d = 0; long cap = a.Length * 4; for (int i = 0; i < a.Length; i++) { d += System.Math.Abs(a[i] - b[i]); if (d > cap) return false; } return true; }
    private bool _togglesLoaded;
    private void SyncOcrFromToggles()
    {
        _ocr.MultiPass = MultiPass;
        _ocr.EntityMinScore = (float)EntityMinScore;
        _ocr.TextMinScore = (float)TextMinScore;
        _ocr.NameEntitiesByText = GrfNamesAbove;
        _ocr.NameEntitiesByIcon = !GrfNamesAbove;
        _ocr.VisionAssistGrf = VisionAssistGrf;
        _ocr.VisionAssistManifestPath = "";
    }

    private string ResolveVisionAssistManifestPath()
        => "";

    private IEnumerable<string> VisionAssistManifestCandidates()
    {
        string appBase = AppContext.BaseDirectory;
        yield return System.IO.Path.Combine(appBase, "VisionAssist.manifest.json");
        yield return System.IO.Path.Combine(Environment.CurrentDirectory, "VisionAssist.manifest.json");

        var gameFolder = _settings.Current.GameFolder;
        if (!string.IsNullOrWhiteSpace(gameFolder))
            yield return System.IO.Path.Combine(gameFolder, "VisionAssist.manifest.json");

        foreach (var root in RepoRootCandidates(appBase).Concat(RepoRootCandidates(Environment.CurrentDirectory)))
            yield return System.IO.Path.Combine(root, "tools", "ocr-train", "Grf", "output", "VisionAssist.manifest.json");
    }

    private static IEnumerable<string> RepoRootCandidates(string start)
    {
        var dir = new System.IO.DirectoryInfo(start);
        while (dir != null)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }
    private void PersistToggles()
    {
        if (!_togglesLoaded) return;
        var t = _settings.Current.OcrToggles;
        t.DetectText = DetectText; t.DetectMonsters = DetectMonsters; t.DetectSkills = DetectSkills; t.DetectMovement = DetectMovement;
        t.ZoneScan = ZoneScan; t.ZoneCols = ZoneCols; t.ZoneRows = ZoneRows;
        t.MultiPass = MultiPass; t.WindowsForNumbers = WindowsForNumbers; t.Ensemble = Ensemble; t.SkipUnchanged = SkipUnchanged;
        t.GrfNamesAbove = GrfNamesAbove; t.IconCellPx = IconCellPx; t.OverlayValuesSize = OverlayValuesSize;
        t.VisionAssistGrf = VisionAssistGrf; t.VisionAssistManifestPath = "";
        t.EntityMinScore = EntityMinScore; t.TextMinScore = TextMinScore;
        t.AutoEntityMinScore = AutoEntityMinScore; t.AutoTextMinScore = AutoTextMinScore;
        t.FastMonsterTracking = FastMonsterTracking;
        t.TextScanEvery = TextScanEvery < 0 ? -1 : Math.Clamp(TextScanEvery, 1, 60);
        t.OverlayFrameMs = OverlayFrameMs < 0 ? -1 : Math.Clamp(OverlayFrameMs, 0, 1000);
        t.MaxOverlayDetections = Math.Clamp(MaxOverlayDetections, 0, 300);
        _settings.Save();
    }
    partial void OnDetectTextChanged(bool value) => PersistToggles();
    partial void OnDetectMonstersChanged(bool value)
    {
        if (value && VisionAssistGrf)
        {
            DetectMonsters = false;
            return;
        }
        PersistToggles();
    }
    partial void OnDetectSkillsChanged(bool value) => PersistToggles();
    partial void OnDetectMovementChanged(bool value) => PersistToggles();
    partial void OnZoneScanChanged(bool value) => PersistToggles();
    partial void OnZoneColsChanged(int value) => PersistToggles();
    partial void OnZoneRowsChanged(int value) => PersistToggles();
    partial void OnWindowsForNumbersChanged(bool value) => PersistToggles();
    partial void OnEnsembleChanged(bool value) => PersistToggles();
    partial void OnSkipUnchangedChanged(bool value) => PersistToggles();
    partial void OnFastMonsterTrackingChanged(bool value) => PersistToggles();
    partial void OnTextScanEveryChanged(int value) => PersistToggles();
    partial void OnOverlayFrameMsChanged(int value) => PersistToggles();
    partial void OnMaxOverlayDetectionsChanged(int value) => PersistToggles();
    partial void OnIconCellPxChanged(int value) => PersistToggles();
    partial void OnOverlayValuesSizeChanged(double value) => PersistToggles();
    partial void OnVisionAssistGrfChanged(bool value)
    {
        if (value && DetectMonsters)
            DetectMonsters = false;
        SyncOcrFromToggles();
        ResetEntityTracking("vision assist grf toggled");
        PersistToggles();
    }
    partial void OnVisionAssistManifestPathChanged(string value) { _ocr.VisionAssistManifestPath = ""; VisionAssistManifestPath = ""; ResetEntityTracking("vision assist marker table changed"); PersistToggles(); }
    partial void OnAutoEntityMinScoreChanged(bool value) { ApplyAutoConfidenceThresholds(); ResetEntityTracking("auto monster confidence toggled"); PersistToggles(); }
    partial void OnAutoTextMinScoreChanged(bool value) { ApplyAutoConfidenceThresholds(); PersistToggles(); }

    private string EngineFor(OcrMark m)
    {
        if (IsPercentTextRole(m.Role)) return "Paddle";
        return !string.IsNullOrEmpty(m.Engine)
            ? m.Engine
            : (Ensemble ? "Ensemble" : (WindowsForNumbers && !m.IsText ? "Windows" : "Paddle"));
    }
    // Region pipelines: on "Auto" marks, let the role's RegionProfile pick the preprocess.
    private string EffPre(OcrMark m) => string.IsNullOrEmpty(m.Preprocess) || m.Preprocess == "Auto" ? _ocr.SuggestPreprocess(m.Role) : m.Preprocess;
    private double EffScale(OcrMark m) => string.IsNullOrEmpty(m.Preprocess) || m.Preprocess == "Auto" ? _ocr.SuggestScale(m.Role) : 0;

    private List<OcrRegion> BuildScanAnchors(System.Drawing.Bitmap frame)
    {
        var src = _activeMarks.Count > 0 ? _activeMarks : Marks.ToList();
        var anchors = new List<OcrRegion>();
        if (frame == null || src.Count == 0) return anchors;
        int cw = Math.Max(1, frame.Width - 2 * EffSide);
        int ch = Math.Max(1, frame.Height - EffTop - EffSide);
        foreach (var m in src)
        {
            if (m.IsBar || string.IsNullOrWhiteSpace(m.Role)) continue;
            int padX = Math.Max(4, (int)Math.Round(m.W * cw * 0.08));
            int padY = Math.Max(3, (int)Math.Round(m.H * ch * 0.15));
            int x = Math.Clamp(EffSide + (int)(m.X * cw) - padX, 0, frame.Width - 1);
            int y = Math.Clamp(EffTop + (int)(m.Y * ch) - padY, 0, frame.Height - 1);
            int w = Math.Clamp((int)(m.W * cw) + padX * 2, 1, frame.Width - x);
            int h = Math.Clamp((int)(m.H * ch) + padY * 2, 1, frame.Height - y);
            anchors.Add(new OcrRegion { Name = m.Role, X = x, Y = y, Width = w, Height = h });
        }
        return anchors;
    }

    [ObservableProperty] private int _zoneCols = 2;
    [ObservableProperty] private int _zoneRows = 2;
    [ObservableProperty] private int _iconCellPx = 32;   // skill/buff icon cell size for SkillBar/BuffBar marks
    [ObservableProperty] private bool _grfNamesAbove;   // GRF shows monster names above heads -> read text, else recognise sprite
    [ObservableProperty] private bool _visionAssistGrf; // Vision Assist GRF: red boxes + baked mob id cells.
    [ObservableProperty] private string _visionAssistManifestPath = "";
    [ObservableProperty] private double _overlayValuesSize = 1.0;   // size of the on-client detected-values panel
    [ObservableProperty] private double _entityMinScore = FourRVivi.Core.Ocr.VisionConfig.DefaultTrackConfidence;   // monster detection confidence floor
    [ObservableProperty] private double _textMinScore = 0.30;     // text/region confidence floor (RO tiny text scores low)
    [ObservableProperty] private bool _autoEntityMinScore = true;
    [ObservableProperty] private bool _autoTextMinScore = true;
    [ObservableProperty] private bool _lockValues = true;   // hold the last confident value when a read fails
    [ObservableProperty] private int _exclX;   // HUD exclusion zone (capture px) ignored by detectors
    [ObservableProperty] private int _exclY;
    [ObservableProperty] private int _exclW;
    [ObservableProperty] private int _exclH;
    partial void OnEntityMinScoreChanged(double value) { _ocr.EntityMinScore = (float)value; ResetEntityTracking($"monster confidence changed to {value:0.00}"); if (!AutoEntityMinScore) PersistToggles(); }
    partial void OnTextMinScoreChanged(double value) { _ocr.TextMinScore = (float)value; if (!AutoTextMinScore) PersistToggles(); }
    partial void OnExclXChanged(int value) => _ocr.ExclX = value;
    partial void OnExclYChanged(int value) => _ocr.ExclY = value;
    partial void OnExclWChanged(int value) => _ocr.ExclW = value;
    partial void OnExclHChanged(int value) => _ocr.ExclH = value;
    partial void OnGrfNamesAboveChanged(bool value) { SyncOcrFromToggles(); PersistToggles(); }
    [ObservableProperty] private string _selectedRole = "HP % Text";
    partial void OnSelectedRoleChanged(string value) => OnPropertyChanged(nameof(SelectedRoleNote));
    public string SelectedRoleNote => RoleNotes.TryGetValue(SelectedRole ?? "", out var n) ? n
        : "Drag the box tightly over this value.";

    private static bool IsTextRole(string role)
        => role is "CharName" or "ClassName" or "BasicInfo" or "MapName" or "Monster" or "TargetName" or "PetName" or "Loot" or "Posture" or "ItemName" or "SkillName";

    private static string NormalizeMarkRole(string role) => role switch
    {
        "HP % Text" => FourRVivi.Core.Game.Roles.HpPercent,
        "SP % Text" => FourRVivi.Core.Game.Roles.SpPercent,
        "HP Bar" => FourRVivi.Core.Game.Roles.HpPercent,
        "SP Bar" => FourRVivi.Core.Game.Roles.SpPercent,
        _ => role
    };

    private static bool IsPercentTextRole(string role)
    {
        role = NormalizeMarkRole(role);
        return role is FourRVivi.Core.Game.Roles.HpPercent or FourRVivi.Core.Game.Roles.SpPercent;
    }

    private static bool IsBarRole(string role)
    {
        role = NormalizeMarkRole(role);
        return role is "BaseExpBar" or "JobExpBar" or "CastBar";
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> RoleNotes = new()
    {
        ["BasicInfo"] = "TEXT block - whole basic-info panel.",
        ["HP % Text"] = "TEXT - draw tightly around the visible 100% text next to the HP bar.",
        ["SP % Text"] = "TEXT - draw tightly around the visible 100% text next to the SP bar.",
        ["HP Bar"] = "OLD MARKER - redraw around the visible 100% text next to the HP bar.",
        ["SP Bar"] = "OLD MARKER - redraw around the visible 100% text next to the SP bar.",
        ["HP / MaxHP"] = "LEGACY NUMBER - current / max HP readout.",
        ["SP / MaxSP"] = "LEGACY NUMBER - current / max SP readout.",
        ["Weight / MaxWeight"] = "NUMBER - current / max weight readout.",
        ["BaseLevel"] = "NUMBER - base level digits.",
        ["JobLevel"] = "NUMBER - job level digits.",
        ["HP"] = "NUMBER - HP digits.",
        ["MaxHP"] = "NUMBER - Max HP digits.",
        ["SP"] = "NUMBER - SP digits.",
        ["MaxSP"] = "NUMBER - Max SP digits.",
        ["HpPercent"] = "TEXT - draw tightly around the visible 100% text next to the HP bar.",
        ["SpPercent"] = "TEXT - draw tightly around the visible 100% text next to the SP bar.",
        ["BaseExpBar"] = "BAR - base EXP bar fill.",
        ["JobExpBar"] = "BAR - job EXP bar fill.",
        ["Character"] = "CHARACTER BOX - your character sprite.",
        ["Weight"] = "NUMBER - weight digits.",
        ["MaxWeight"] = "NUMBER - max weight digits.",
        ["Zeny"] = "NUMBER - zeny amount.",
        ["Loot"] = "TEXT - loot or pick-up message line.",
        ["PosX"] = "NUMBER - X coordinate readout.",
        ["PosY"] = "NUMBER - Y coordinate readout.",
        ["CharName"] = "TEXT - character name.",
        ["ClassName"] = "TEXT - class/job name.",
        ["MapName"] = "TEXT - current map name.",
        ["Monster"] = "TEXT - monster name label above its head.",
        ["Posture"] = "TEXT - posture/status word.",
        ["ItemName"] = "TEXT - item name.",
        ["SkillName"] = "TEXT - skill name.",
        ["Ammo"] = "NUMBER - ammo / rounds-left counter.",
        ["TargetName"] = "TEXT - target monster name.",
        ["TargetHP"] = "NUMBER or BAR - target HP readout.",
        ["PetName"] = "TEXT - pet/homun name.",
        ["SkillBar"] = "ICON STRIP - row of skill icons.",
        ["BuffBar"] = "ICON STRIP - row of buff icons.",
        ["StatusIcons"] = "ICON STRIP - status-effect icon row.",
        ["CastBar"] = "BAR - cast/progress bar.",
    };
    [ObservableProperty] private bool _running;
    [ObservableProperty] private bool _calibrating;
    [ObservableProperty] private bool _calibrateUntilComplete;
    public string[] MarkerModes { get; } = { "Auto-detected", "Review detected", "Manual" };
    [ObservableProperty] private string _markerMode = "Review detected";
    [ObservableProperty] private string _markerConfidenceStatus = "Review markers before starting OCR.";
    [ObservableProperty] private bool _canUseDetectedMarkers;
    private volatile bool _calibCancel;
    private byte[]? _lastChar;
    [ObservableProperty] private string _engineInfo = "engine: -";
    [ObservableProperty] private string _status = "1) Load a screenshot. 2) Pick a stat, drag a box over it. 3) Save. 4) Start.";

    private readonly FourRVivi.Core.Game.OcrNameCorrector _corrector = new();
    private readonly FourRVivi.Core.Ocr.TemporalVotingService _vote = new();  // Last-N frame majority vote.

    public OcrReaderViewModel(GameSession session, OcrService ocr, SettingsStore settings, DiscordPresenceUpdater discord, EngineHub hub,
                              Lazy<FourRVivi.Core.Data.GameDatabase> db)
    {
        _session = session; _ocr = ocr; _settings = settings; _discord = discord; _hub = hub;
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
        _onnxExecutionProvider = NormalizeOnnxProvider(settings.Current.OcrTuning.OnnxExecutionProvider);
        _readRotated = settings.Current.OcrTuning.DoAngle;
        _limitSide = settings.Current.OcrTuning.LimitSideLen;
        var tg = settings.Current.OcrToggles;
        if (Math.Abs(tg.EntityMinScore - 0.55) < 0.0001 || Math.Abs(tg.EntityMinScore - 0.40) < 0.0001 || Math.Abs(tg.EntityMinScore - 0.35) < 0.0001)
        {
            tg.EntityMinScore = FourRVivi.Core.Ocr.VisionConfig.DefaultTrackConfidence;
            settings.Save();
        }
        if (!tg.DetectText && !tg.DetectMonsters && !tg.DetectSkills)
        {
            tg.DetectText = true;
            tg.DetectMonsters = true;
            tg.DetectSkills = true;
            tg.MultiPass = true;
            tg.WindowsForNumbers = true;
            settings.Save();
        }
        _detectText = tg.DetectText; _detectMonsters = tg.DetectMonsters; _detectSkills = tg.DetectSkills; _detectMovement = tg.DetectMovement;
        _zoneScan = tg.ZoneScan; _zoneCols = tg.ZoneCols; _zoneRows = tg.ZoneRows;
        _multiPass = tg.MultiPass; _windowsForNumbers = tg.WindowsForNumbers; _ensemble = tg.Ensemble; _skipUnchanged = tg.SkipUnchanged;
        _grfNamesAbove = tg.GrfNamesAbove; _iconCellPx = tg.IconCellPx; _overlayValuesSize = tg.OverlayValuesSize;
        _visionAssistGrf = tg.VisionAssistGrf; _visionAssistManifestPath = tg.VisionAssistManifestPath ?? "";
        _entityMinScore = tg.EntityMinScore; _textMinScore = tg.TextMinScore;
        _autoEntityMinScore = tg.AutoEntityMinScore; _autoTextMinScore = tg.AutoTextMinScore;
        _fastMonsterTracking = tg.FastMonsterTracking;
        _textScanEvery = tg.TextScanEvery < 0 ? -1 : Math.Clamp(tg.TextScanEvery <= 0 ? -1 : tg.TextScanEvery, 1, 60);
        if (tg.OverlayFrameMs is 8 or 33 or 75 or 100)
        {
            tg.OverlayFrameMs = -1;
            settings.Save();
        }
        _overlayFrameMs = tg.OverlayFrameMs < 0 ? -1 : Math.Clamp(tg.OverlayFrameMs, 0, 1000);
        _maxOverlayDetections = Math.Clamp(tg.MaxOverlayDetections <= 0 ? 40 : tg.MaxOverlayDetections, 1, 300);
        SyncOcrFromToggles();
        _togglesLoaded = true;
        foreach (var m in settings.Current.OcrMarks)
        {
            m.Role = NormalizeMarkRole(m.Role);
            if (IsPercentTextRole(m.Role)) { m.IsBar = false; m.IsText = false; }
            else if (IsBarRole(m.Role)) m.IsBar = true;
            Marks.Add(m);
        }
        Marks.CollectionChanged += (_, _) => EvaluateMarkerReadiness();
        EvaluateMarkerReadiness();
        try { _hook = new GlobalKeyHook(); _hook.KeyPressed += OnGlobalKey; } catch { }
    }

    [ObservableProperty] private double _detBoxThresh;
    [ObservableProperty] private double _detUnclip;
    [ObservableProperty] private int _ocrCpuThreads;
    public string[] OnnxExecutionProviders { get; } = { "auto", "cpu", "cuda", "directml", "amd/directml" };
    [ObservableProperty] private string _onnxExecutionProvider = "auto";
    partial void OnDetBoxThreshChanged(double value) { _ocr.Tuning.DetBoxThresh = (float)value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnDetUnclipChanged(double value) { _ocr.Tuning.DetUnclipRatio = (float)value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnOcrCpuThreadsChanged(int value) { _ocr.Tuning.CpuThreads = value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnOnnxExecutionProviderChanged(string value)
    {
        value = NormalizeOnnxProvider(value);
        _ocr.Tuning.OnnxExecutionProvider = value;
        _settings.Current.OcrTuning.OnnxExecutionProvider = value;
        _settings.Save();
        _ocr.ReloadWorker();
    }
    [ObservableProperty] private bool _readRotated;
    [ObservableProperty] private int _limitSide;
    partial void OnReadRotatedChanged(bool value) { _ocr.Tuning.DoAngle = value; _ocr.ApplyTuning(); _settings.Save(); }
    partial void OnLimitSideChanged(int value) { _ocr.Tuning.LimitSideLen = Math.Max(64, value); _ocr.ApplyTuning(); _settings.Save(); }

    private static string NormalizeOnnxProvider(string? value)
    {
        var v = (value ?? "auto").Trim().ToLowerInvariant();
        return v is "cpu" or "cuda" or "nvidia" or "gpu" or "directml" or "dml" or "amd" or "amd/directml" or "amd directml"
            ? (v is "nvidia" or "gpu" ? "cuda" : v is "dml" or "amd" or "amd/directml" or "amd directml" ? "directml" : v)
            : "auto";
    }

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
        Status = ok ? "Training done - new model installed. Reloading OCR." : "Training cancelled or failed (old model kept).";
        if (ok) _ocr.ReloadWorker();
    }

    private void OnTrainLine(string l) => Dispatcher.UIThread.Post(() => TrainLog += l + "\n");

    [RelayCommand] private void CancelTrain() { _trainCts?.Cancel(); }

    partial void OnMarkerModeChanged(string value)
    {
        EvaluateMarkerReadiness();
        if (string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase))
            Status = "Manual marker mode: capture the screen, pick a role, drag boxes, then Save.";
        else if (string.Equals(value, "Auto-detected", StringComparison.OrdinalIgnoreCase))
            Status = CanUseDetectedMarkers
                ? "Auto-detected markers are ready. Press Use detected markers, then Start."
                : "Auto-detected markers need review first. Capture/read once or draw the missing core marks.";
    }

    [RelayCommand]
    private void UseDetectedMarkers()
    {
        EvaluateMarkerReadiness();
        if (!CanUseDetectedMarkers)
        {
            Status = MarkerConfidenceStatus;
            return;
        }

        _settings.Current.OcrMarks = Marks.ToList();
        _settings.Save();
        MarkerMode = "Auto-detected";
        Status = $"Using {Marks.Count} detected/reviewed marker(s). Start OCR when ready.";
    }

    [RelayCommand]
    private async Task AutoDetectMarkers()
    {
        if (UseMonitor && SelectedMonitor == null) { Status = "Pick a monitor first."; return; }
        if (!UseMonitor && _session.WindowHandle == IntPtr.Zero) { Status = "Pick your RO process first, or switch to monitor capture."; return; }

        Status = "Scanning the current capture for HP/SP/Weight markers...";
        if (!await _ocr.EnsureDataAsync()) { Status = "Couldn't get OCR data (no internet?)."; return; }

        using var frame = UseMonitor ? GrabMonitor() : GrabWindow();
        if (frame == null) { Status = "Could not capture the screen for marker detection."; return; }

        bool oldText = _ocr.ScanTextEnabled;
        bool oldEntities = _ocr.ScanEntitiesEnabled;
        try
        {
            _ocr.ScanTextEnabled = true;
            _ocr.ScanEntitiesEnabled = false;
            var finds = (ZoneScan ? _ocr.ScanScreenZoned(frame, ZoneCols, ZoneRows) : _ocr.ScanScreen(frame))
                .Where(f => f.Kind == "Text" && OcrService.ParseTwoInts(f.Value) != null)
                .Where(f => f.W >= 20 && f.H >= 8)
                .OrderBy(f => f.Y)
                .ThenBy(f => f.X)
                .ToList();

            if (finds.Count == 0)
            {
                MarkerMode = "Manual";
                Status = "No reliable HP/SP-style numeric markers were found. Use Manual mode and draw the boxes.";
                EvaluateMarkerReadiness();
                return;
            }

            var roles = new[] { "Weight / MaxWeight" };
            int added = 0;
            for (int i = 0; i < Math.Min(roles.Length, finds.Count); i++)
            {
                var f = finds[i];
                if (AddOrReplaceAutoMark(roles[i], f, frame.Width, frame.Height)) added++;
            }

            MarkerMode = "Review detected";
            _settings.Current.OcrMarks = Marks.ToList();
            _settings.Save();
            EvaluateMarkerReadiness();
            Status = added > 0
                ? $"Suggested {added} marker(s). Review the boxes, adjust if needed, then run one OCR read."
                : "No marker changes were needed. Review the boxes, then run one OCR read.";
        }
        finally
        {
            _ocr.ScanTextEnabled = oldText;
            _ocr.ScanEntitiesEnabled = oldEntities;
        }
    }

    private bool AddOrReplaceAutoMark(string role, OcrService.ScanFind f, int frameW, int frameH)
    {
        if (frameW <= 0 || frameH <= 0) return false;
        int padX = Math.Max(4, f.W / 8);
        int padY = Math.Max(3, f.H / 4);
        double x = Math.Clamp((f.X - padX) / (double)frameW, 0, 1);
        double y = Math.Clamp((f.Y - padY) / (double)frameH, 0, 1);
        double w = Math.Clamp((f.W + padX * 2) / (double)frameW, 0.004, 1 - x);
        double h = Math.Clamp((f.H + padY * 2) / (double)frameH, 0.004, 1 - y);

        foreach (var old in Marks.Where(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase)).ToList())
            Marks.Remove(old);

        Marks.Add(new OcrMark
        {
            Role = role,
            X = x,
            Y = y,
            W = w,
            H = h,
            IsText = false,
            IsBar = false,
            IsIcons = false,
            MinScore = Math.Max(0.30, Math.Min(0.85, f.Score)),
            Preprocess = "Auto",
        });
        return true;
    }

    private void EvaluateMarkerReadiness()
    {
        var required = new[] { FourRVivi.Core.Game.Roles.HpPercent, FourRVivi.Core.Game.Roles.SpPercent };
        bool HasRole(string role)
            => Marks.Any(m => string.Equals(NormalizeMarkRole(m.Role), role, StringComparison.OrdinalIgnoreCase));

        var missing = required.Where(r => !HasRole(r)).ToList();
        if (missing.Count > 0)
        {
            CanUseDetectedMarkers = false;
            MarkerConfidenceStatus = "Needs user: draw " + string.Join(", ", missing.Select(r => r == FourRVivi.Core.Game.Roles.HpPercent ? "HP % Text" : "SP % Text")) + " marker(s).";
            return;
        }

        var checkedRows = _rows.Values
            .Where(r => required.Any(req => string.Equals(r.Role, req, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (checkedRows.Count == 0)
        {
            CanUseDetectedMarkers = false;
            MarkerConfidenceStatus = $"Review: {Marks.Count} marker(s) saved. Run one OCR read to confirm confidence.";
            return;
        }

        bool confident = checkedRows.All(r => TryConf(r.Conf, out var c) && c >= 0.85);
        CanUseDetectedMarkers = confident;
        MarkerConfidenceStatus = confident
            ? $"Ready: core markers are present and confident ({checkedRows.Count} live readout checks)."
            : "Review: one or more core markers read below 85% confidence. Adjust manually or calibrate.";
    }

    private static bool TryConf(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimEnd('%');
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
        if (value > 1) value /= 100.0;
        return true;
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
        role = NormalizeMarkRole(role);
        bool isText = IsTextRole(role);
        bool isBar = IsBarRole(role);
        bool isChar = role == "Character";
        bool isIcons = role is "SkillBar" or "BuffBar" or "StatusIcons";
        double minScore = role switch
        {
            "Monster" or "TargetName" => 0.40,
            "ItemName" or "SkillName" => 0.45,
            "CharName" or "ClassName" or "PetName" => 0.50,
            "MapName" => 0.55,
            FourRVivi.Core.Game.Roles.HpPercent or FourRVivi.Core.Game.Roles.SpPercent => 0.35,
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
        Status = "Preparing OCR (first run downloads language data).";
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
            int period = ResolveOcrLoopDelayMs();
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => BgTick(), null, 250, System.Threading.Timeout.Infinite);   // OFF the UI thread
            Status = IntervalMs < 0
                ? $"Running capture in Auto timing (~{period}ms between OCR passes). Top bar, Stats and Discord now use it."
                : $"Running capture every {period}ms (background). Top bar, Stats and Discord now use it.";
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

    [RelayCommand] private void ResetOcrAutoDefaults()
    {
        IntervalMs = -1;
        TextScanEvery = -1;
        OverlayFrameMs = -1;
        AutoEntityMinScore = true;
        AutoTextMinScore = true;
        DetectText = true;
        DetectMonsters = true;
        DetectSkills = true;
        DetectMovement = true;
        FastMonsterTracking = true;
        UseMonitor = false;
        UseDxgiCapture = true;
        MultiPass = true;
        WindowsForNumbers = true;
        Ensemble = true;
        SkipUnchanged = true;
        ZoneScan = false;
        ZoneCols = 2;
        ZoneRows = 2;
        MaxOverlayDetections = 40;
        IconCellPx = 32;
        OverlayValuesSize = 1.0;
        ApplyAutoConfidenceThresholds();
        ResetEntityTracking("OCR defaults restored");
        PersistToggles();
        Status = "OCR defaults restored: Auto timing, Auto confidence, client-window capture, fast monster tracking, and stable text voting.";
    }

    private void ResetEntityTracking(string reason)
    {
        try
        {
            LiveScene.Instance.Clear();
            Dispatcher.UIThread.Post(() => _overlay?.ClearEntityTracks(reason));
            FourRVivi.Core.Common.DebugTrace.Write("OCR", $"Entity tracking reset reason='{reason}' entityMin={EntityMinScore:0.00} textMin={TextMinScore:0.00} autoEntity={AutoEntityMinScore} detectMonsters={DetectMonsters}.");
        }
        catch (Exception ex)
        {
            FourRVivi.Core.Common.DebugTrace.Write("OCR", $"Entity tracking reset failed reason='{reason}'.", ex);
        }
    }

    private void ScheduleNextOcrTick()
    {
        if (!Running || !RunningCapture)
            return;
        try { _timer?.Change(ResolveOcrLoopDelayMs(), System.Threading.Timeout.Infinite); }
        catch { }
    }

    private int ResolveOcrLoopDelayMs()
    {
        if (IntervalMs >= 0)
            return Math.Clamp(IntervalMs, 1, 2000);

        bool scanEntities = ShouldScanEntitiesForSmartBot();
        double entity = _entityScanEmaMs > 1 ? _entityScanEmaMs : (scanEntities ? 120 : 0);
        double text = _textScanEmaMs > 1 ? _textScanEmaMs : (DetectText ? 180 : 0);
        double tick = _tickEmaMs > 1 ? _tickEmaMs : Math.Max(entity, text);
        int sceneAge = FourRVivi.Core.Game.LiveScene.Instance.EntityUpdatedUtc == DateTime.MinValue
            ? 500
            : (int)Math.Clamp((DateTime.UtcNow - FourRVivi.Core.Game.LiveScene.Instance.EntityUpdatedUtc).TotalMilliseconds, 0, 1000);
        int staleBoost = sceneAge > 450 ? -18 : 0;
        int textLoad = DetectText && !FastMonsterTracking ? (int)Math.Round(text * 0.15) : 0;
        int auto = (int)Math.Round(18 + entity * 0.22 + tick * 0.08 + textLoad + staleBoost);
        return Math.Clamp(auto, 12, scanEntities ? 140 : 300);
    }

    private bool ShouldScanEntitiesForSmartBot()
        => DetectMonsters || (VisionAssistGrf && _hub.SmartBot.Enabled && _hub.SmartBot.UseVision);

    private int ResolveTextScanEvery()
    {
        if (TextScanEvery > 0)
            return Math.Clamp(TextScanEvery, 1, 60);
        if (!FastMonsterTracking)
            return 1;

        double entity = _entityScanEmaMs > 1 ? _entityScanEmaMs : 120;
        double period = Math.Max(25, entity + ResolveOcrLoopDelayMs());
        int every = (int)Math.Round(850.0 / period);
        return Math.Clamp(every, 2, 14);
    }

    private int ResolveOverlayFrameMs()
    {
        if (OverlayFrameMs >= 0)
            return Math.Clamp(OverlayFrameMs, 0, 1000);

        double entity = _entityScanEmaMs > 1 ? _entityScanEmaMs : 120;
        double tick = _tickEmaMs > 1 ? _tickEmaMs : entity;
        return Math.Clamp((int)Math.Round(12 + entity * 0.18 + tick * 0.05), 16, 90);
    }

    private void ApplyAutoConfidenceThresholds()
    {
        bool changed = false;
        if (AutoEntityMinScore)
        {
            double value = FourRVivi.Core.Ocr.VisionConfig.DefaultTrackConfidence;
            if (Math.Abs(EntityMinScore - value) >= 0.005)
            {
                EntityMinScore = value;
                changed = true;
            }
            _ocr.EntityMinScore = (float)value;
        }
        if (AutoTextMinScore)
        {
            double value = ResolveAutoTextMinScore();
            if (Math.Abs(TextMinScore - value) >= 0.005)
            {
                TextMinScore = value;
                changed = true;
            }
            _ocr.TextMinScore = (float)value;
        }
        if (changed)
            FourRVivi.Core.Common.DebugTrace.Write("OCR", $"Auto confidence entity={_ocr.EntityMinScore:0.00} text={_ocr.TextMinScore:0.00} entityMs={_entityScanEmaMs:0} textMs={_textScanEmaMs:0} tickMs={_tickEmaMs:0}.");
    }

    private double ResolveAutoEntityMinScore()
    {
        return FourRVivi.Core.Ocr.VisionConfig.DefaultTrackConfidence;
    }

    private double ResolveAutoTextMinScore()
    {
        double text = _textScanEmaMs > 1 ? _textScanEmaMs : 180;
        double score = 0.30;
        if (WindowsForNumbers) score -= 0.03;
        if (MultiPass || Ensemble) score -= 0.02;
        if (text > 450) score += 0.03;
        if (FastMonsterTracking) score -= 0.02;
        return Math.Round(Math.Clamp(score, 0.18, 0.45), 2);
    }

    private static double Ema(double current, double sample, double alpha)
        => current <= 0 ? sample : current * (1.0 - alpha) + sample * alpha;

    public void Shutdown()
    {
        try { _timer?.Dispose(); } catch { }
        _timer = null;
        Running = false;
        Calibrating = false;
        _calibCancel = true;
        LiveStats.Instance.Active = false;
        LiveScene.Instance.Clear();
        OverlayOn = false;
        HideOverlay();
        try { _ocr.Dispose(); } catch { }
        Status = "OCR stopped for shutdown.";
    }

    /// <summary>Cycle through EVERY filter (and grow the search radius) nudging boxes, recording which
    /// filter+offset reads each field; then lock in the filter that covers the most. Press again to stop.</summary>
    [RelayCommand] private async Task Calibrate()
    {
        if (Calibrating) { _calibCancel = true; Status = "Stopping calibration."; return; }
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
                    Dispatcher.UIThread.Post(() => Status = $"Per-mark tuning: {cov}/{fields} locked (radius {radius}).");
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
                    ? $"Done - per-mark filter+sharpness locked for all {fields} fields; nudged {moved}. Saved. Press Start."
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
        if (Calibrating) { _calibCancel = true; Status = "Stopping calibration."; return; }
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
                Dispatcher.UIThread.Post(() => Status = $"Calibrating to your values. {dd}/{tt} ({mm} matched).");
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
            PushWalkBoxOverlay();
            _overlay.Show();
        }
        catch { _overlay = null; }
    }

    private void PushWalkBoxOverlay()
    {
        if (_overlay == null) return;
        var b = _hub.SmartBot;
        var cfg = _settings.Current.GetActiveProfile().SmartBot;
        bool show = cfg.ShowWalkBoxOverlay && b.UseWalkBox && b.BoxW > 2 && b.BoxH > 2;
        _overlay.SetWalkBox(show ? new WalkBox(b.BoxX, b.BoxY, b.BoxW, b.BoxH) : null);
    }

    private void HideOverlay()
    {
        try { _overlay?.Close(); } catch { }
        _overlay = null;
    }

    private void HarvestHardExample(System.Drawing.Bitmap? frame, OcrMark m, string raw, double conf, string reason)
    {
        if (frame == null) return;
        _ocr.SaveHardExample(frame, m, raw, conf, reason, EffTop, EffSide);
    }

    private void BgTick()
    {
        if (_busy)
        {
            ScheduleNextOcrTick();
            return;
        }
        long tickStarted = Environment.TickCount64;
        _busy = true;
        try
        {
            var hwnd = _session.WindowHandle;
            if (hwnd == IntPtr.Zero) { Dispatcher.UIThread.Post(() => Status = "Lost the game window. Is it still open?"); return; }
            if (!_hub.FocusGate.CanRead(out var focus))
            {
                LiveStats.Instance.Clear();
                FourRVivi.Core.Game.LiveScene.Instance.Clear();
                long now = Environment.TickCount64;
                if (now - _lastFocusGateMs > 1000)
                {
                    _lastFocusGateMs = now;
                    DebugTrace.Write("OCR", $"Paused: selected client is not capturable hwnd=0x{hwnd.ToInt64():X} reason='{focus.Reason}'.");
                    Dispatcher.UIThread.Post(() => Status = $"OCR paused - attached RO client is not capturable ({focus.Reason}).");
                }
                return;
            }

            LiveStats.Instance.Touch();   // heartbeat so consumers stay "live" even if a read fails
            using var frame = CaptureFrame(hwnd);   // monitor or window
            var rowData = new System.Collections.Generic.List<(string role, string val, string conf)>();
            foreach (var m in _activeMarks)
            {
                if (IsTextRole(m.Role) && !m.IsText) m.IsText = true;
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
                if (IsPercentTextRole(m.Role) && m.IsBar)
                {
                    m.IsBar = false;
                    DebugTrace.Write("Vitals", $"{m.Role} legacy bar mark converted to percent-text reader.");
                }
                if (m.IsBar)
                {
                    int pct = frame != null ? _ocr.ReadBarPercentFrom(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide, m.Role) : _ocr.ReadBarPercent(hwnd, m.X, m.Y, m.W, m.H, TopPx, SidePx, m.Role);
                    if (pct >= 0)
                        pct = SmoothBarPercent(m.Role, pct);
                    var stB = Resolve(m.Role, pct >= 0);
                    if (stB == LockState.Publish) { LiveStats.Instance.SetNumber(m.Role, pct); rowData.Add((m.Role, pct + "%", "")); }
                    else if (stB != LockState.Miss && LiveStats.Instance.TryGetNumber(m.Role, out var lp)) { LiveStats.Instance.SetNumber(m.Role, lp); rowData.Add((m.Role, lp + "%", "user")); }
                    else rowData.Add((m.Role, "?", ""));
                    continue;
                }
                if (IsPercentTextRole(m.Role))
                {
                    int pct = -1;
                    string percentRaw = "";
                    string normalized = "";
                    double percentConf = 0;
                    string usedEngine = "";
                    if (frame != null)
                    {
                        pct = _ocr.ReadPercentTextFrom(frame, m.X, m.Y, m.W, m.H, EffTop, EffSide, m.Role, EngineFor(m), out percentRaw, out normalized, out percentConf, out usedEngine);
                    }
                    else
                    {
                        percentRaw = _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: false, topOffset: TopPx, sideOffset: SidePx);
                        percentConf = _ocr.LastRecScore;
                        usedEngine = _ocr.LastEngine;
                        if (!OcrService.TryParsePercentText(percentRaw, percentConf, out pct, out normalized))
                            pct = -1;
                    }

                    var decision = "reject";
                    if (pct >= 0 && ShouldPublishPercent(m.Role, pct, percentConf))
                    {
                        pct = SmoothBarPercent(m.Role, pct);
                        LiveStats.Instance.SetNumber(m.Role, pct, LiveStatSource.PercentText, percentConf, percentRaw, LiveStatQuality.Trusted);
                        rowData.Add((m.Role, pct + "%", percentConf.ToString("0.00")));
                        decision = "publish";
                    }
                    else if (LiveStats.Instance.TryGetNumber(m.Role, out var lastValue))
                    {
                        LiveStats.Instance.HoldNumber(m.Role, lastValue, LiveStatSource.PercentText, percentConf, percentRaw);
                        rowData.Add((m.Role, lastValue + "%", "held"));
                        decision = "hold";
                    }
                    else
                    {
                        rowData.Add((m.Role, "?", percentConf > 0 ? percentConf.ToString("0.00") : ""));
                    }

                    DebugTrace.Write("Vitals",
                        $"{m.Role} pct={(pct >= 0 ? pct.ToString() : "?")} src=percentText engine='{usedEngine}' " +
                        $"conf={percentConf:0.00} raw='{percentRaw}' norm='{normalized}' decision={decision} " +
                        $"quality={(decision == "publish" ? LiveStatQuality.Trusted : decision == "hold" ? LiveStatQuality.Held : LiveStatQuality.Suspect)}");
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
                    string two = frame != null ? (MultiPass ? _ocr.ReadRectBest(frame, m.X, m.Y, m.W, m.H, true, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m), EffScale(m)) : _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, true, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m), EffScale(m))) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: true, topOffset: TopPx, sideOffset: SidePx);
                    double confC = _ocr.LastRecScore;
                    var parsed = OcrService.ParseTwoInts(two);
                    var stC = Resolve(m.Role, parsed is { } && (m.MinScore <= 0 || confC >= m.MinScore));
                    if (stC != LockState.Publish) HarvestHardExample(frame, m, two, confC, parsed is null ? "combined-parse-failed" : "combined-low-confidence");
                    if (stC == LockState.Publish && parsed is { } pv) { LiveStats.Instance.SetNumber(pair.a, pv.Item1); LiveStats.Instance.SetNumber(pair.b, pv.Item2); rowData.Add((m.Role, $"{pv.Item1} / {pv.Item2}", "")); }
                    else if (stC != LockState.Miss && LiveStats.Instance.TryGetNumber(pair.a, out var la) && LiveStats.Instance.TryGetNumber(pair.b, out var lb))
                    { LiveStats.Instance.SetNumber(pair.a, la); LiveStats.Instance.SetNumber(pair.b, lb); rowData.Add((m.Role, $"{la} / {lb}", "user")); }
                    else rowData.Add((m.Role, "?", ""));
                    continue;
                }
                string raw = frame != null ? (MultiPass ? _ocr.ReadRectBest(frame, m.X, m.Y, m.W, m.H, !m.IsText, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m), EffScale(m)) : _ocr.ReadRectFrom(frame, m.X, m.Y, m.W, m.H, !m.IsText, EffTop, EffSide, EffPre(m), m.Sharpen, EngineFor(m), EffScale(m))) : _ocr.ReadRect(hwnd, m.X, m.Y, m.W, m.H, numeric: !m.IsText, topOffset: TopPx, sideOffset: SidePx);
                double conf = _ocr.LastRecScore;
                if (m.IsText)
                {
                    string t = raw.Trim();
                    // Snap to a real game name when we have a dictionary for this role (class/map/monster/item/skill).
                    if (t.Length > 0 && _corrector.HasRole(m.Role)) t = _corrector.Correct(m.Role, t);
                    if (t.Length > 0) t = _vote.Vote(m.Role, t);   // temporal voting: one bad frame can't flip the overlay
                    var stT = Resolve(m.Role, t.Length > 0 && (m.MinScore <= 0 || conf >= m.MinScore));
                    if (stT != LockState.Publish) HarvestHardExample(frame, m, raw, conf, string.IsNullOrWhiteSpace(raw) ? "text-empty" : "text-low-confidence");
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
                    if (stN != LockState.Publish) HarvestHardExample(frame, m, raw, conf, n < 0 ? "number-parse-failed" : "number-low-confidence");
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
                            dets.Add(new FourRVivi.App.Overlay.DetBox("Icon", hit.X, hit.Y, hit.W, hit.H, lbl, hit.Score));
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
            bool scanEntitiesForSmartBot = ShouldScanEntitiesForSmartBot();
            bool grfBotOnlyScan = scanEntitiesForSmartBot && !DetectMonsters;
            if ((DetectText || scanEntitiesForSmartBot) && frame != null)
            {
                long tick = Interlocked.Increment(ref _ocrTick);
                ApplyAutoConfidenceThresholds();
                int textScanEvery = ResolveTextScanEvery();
                bool scanTextThisFrame = DetectText
                    && (!FastMonsterTracking || tick % Math.Max(1, textScanEvery) == 0);
                bool useZoneThisFrame = ZoneScan
                    && (!FastMonsterTracking || scanTextThisFrame);
                _ocr.ScanTextEnabled = scanTextThisFrame; _ocr.ScanEntitiesEnabled = scanEntitiesForSmartBot;
                try
                {
                    var ents = new List<FourRVivi.Core.Game.SceneItem>();
                    var hpBars = new List<FourRVivi.Core.Game.SceneItem>();
                    var texts = new List<string>();
                    var anchors = BuildScanAnchors(frame);
                    bool publishedEntities = false;
                    bool sceneClientCoords = !UseMonitor
                        || (UseMonitor && SelectedMonitor != null && _session.WindowHandle != IntPtr.Zero);

                    if (scanEntitiesForSmartBot)
                    {
                        long entityScanStarted = Environment.TickCount64;
                        var entityScan = _ocr.ScanEntitiesOnly(frame);
                        long entityScanMs = Environment.TickCount64 - entityScanStarted;
                        _entityScanEmaMs = Ema(_entityScanEmaMs, entityScanMs, 0.28);
                        bool safeForBotEntities = entityScanMs <= 1400;
                        int sourceGrf = entityScan.Count(e => string.Equals(e.Source, "grf", StringComparison.OrdinalIgnoreCase));
                        int sourceYolo = entityScan.Count(e => string.Equals(e.Source, "yolo", StringComparison.OrdinalIgnoreCase));
                        int sourceOther = entityScan.Count - sourceGrf - sourceYolo;

                        foreach (var fnd in entityScan)
                        {
                            if (fnd.Kind == "Entity")
                            {
                                if (safeForBotEntities && TryBuildSceneItem(fnd, out var item))
                                    ents.Add(item);
                            }
                            else if (fnd.Kind == "EntityHp")
                            {
                                if (safeForBotEntities && TryBuildSceneItem(fnd, out var hp))
                                    hpBars.Add(hp);
                            }
                        }

                        if (!safeForBotEntities)
                        {
                            FourRVivi.Core.Common.DebugTrace.Write("OCR", $"Entity scan took {entityScanMs}ms; overlay kept raw boxes, bot entity publish skipped for click safety.");
                            LogEntityScan(entityScan.Count, ents.Count, hpBars.Count, entityScanMs, safeForBotEntities, sceneClientCoords, capW, capH, published: false, sourceGrf, sourceYolo, sourceOther);
                        }
                        else
                        {
                            FourRVivi.Core.Game.LiveScene.Instance.Active = true;
                            if (VisionAssistGrf)
                                FourRVivi.Core.Game.LiveScene.Instance.SetAuthoritativeEntities(ents, sceneClientCoords, capW, capH, "grf");
                            else
                                FourRVivi.Core.Game.LiveScene.Instance.SetEntities(ents, hpBars, sceneClientCoords, capW, capH);
                            if (DetectMonsters)
                                ReplaceRawEntityBoxesWithTracks(dets);
                            publishedEntities = true;
                            LogEntityScan(entityScan.Count, ents.Count, hpBars.Count, entityScanMs, safeForBotEntities, sceneClientCoords, capW, capH, published: true, sourceGrf, sourceYolo, sourceOther, grfBotOnlyScan);
                        }
                    }

                    if (scanTextThisFrame)
                    {
                        _ocr.ScanTextEnabled = true; _ocr.ScanEntitiesEnabled = false;
                        long textScanStarted = Environment.TickCount64;
                        var textScan = useZoneThisFrame
                            ? _ocr.ScanScreenZoned(frame, ZoneCols, ZoneRows, anchors)
                            : _ocr.ScanScreen(frame, anchors);
                        _textScanEmaMs = Ema(_textScanEmaMs, Environment.TickCount64 - textScanStarted, 0.24);
                        foreach (var fnd in textScan)
                        {
                            if (!string.IsNullOrWhiteSpace(fnd.Value))
                            {
                                texts.Add(fnd.Value);
                                dets.Add(new FourRVivi.App.Overlay.DetBox(fnd.Kind, fnd.X, fnd.Y, fnd.W, fnd.H, fnd.Value, fnd.Score));
                            }
                        }
                    }

                    texts.AddRange(buffStatuses);
                    if (scanTextThisFrame || buffStatuses.Count > 0)
                    {
                        FourRVivi.Core.Game.LiveScene.Instance.Active = true;
                        FourRVivi.Core.Game.LiveScene.Instance.SetStatuses(texts);
                    }
                    if (!publishedEntities && scanEntitiesForSmartBot)
                        FourRVivi.Core.Common.DebugTrace.Write("OCR", "Entity tracks were not replaced this tick because no safe entity publish occurred.");
                }
                catch { }
            }
            else if (buffStatuses.Count > 0)
            {
                FourRVivi.Core.Game.LiveScene.Instance.Active = true;
                FourRVivi.Core.Game.LiveScene.Instance.SetStatuses(buffStatuses);
            }

            string eng = _ocr.LastEngine;
            string runtime = _ocr.RuntimeProvider;
            long runtimeNow = Environment.TickCount64;
            if (runtimeNow - _lastRuntimeRefreshMs >= 2000)
            {
                _lastRuntimeRefreshMs = runtimeNow;
                runtime = _ocr.RefreshRuntimeProvider();
            }
            string warn = _ocr.EngineWarning;
            Dispatcher.UIThread.Post(() =>
            {
                EngineInfo = "engine: " + eng
                    + (string.IsNullOrWhiteSpace(runtime) || runtime == "unknown" ? "" : $"  runtime: {runtime}")
                    + (_ocr.HardExampleCount > 0 ? $"  hard examples: {_ocr.HardExampleCount}" : "")
                    + (string.IsNullOrEmpty(warn) ? "" : "  warning: " + warn);
                if (!string.IsNullOrEmpty(warn)) Status = "Warning: " + warn;
                foreach (var rd in rowData)
                {
                    if (_rows.TryGetValue(rd.role, out var row)) { row.Value = rd.val; row.Conf = rd.conf; }
                    else { var nr = new ReadoutRow { Role = rd.role, Value = rd.val, Conf = rd.conf, Expected = Marks.FirstOrDefault(x => x.Role == rd.role)?.Expected ?? "" }; _rows[rd.role] = nr; Readout.Add(nr); }
                }
                EvaluateMarkerReadiness();
                if (_overlay != null)
                {
                    long now = Environment.TickCount64;
                    int overlayFrameMs = ResolveOverlayFrameMs();
                    if (overlayFrameMs <= 0 || now - _lastOverlayPushMs >= overlayFrameMs)
                    {
                        _lastOverlayPushMs = now;
                        _overlay.SetInfo(Marks.ToList(), _session.Reader.Target?.ProcessName ?? "client", TopPx, SidePx);
                        var overlayDets = MaxOverlayDetections > 0 && dets.Count > MaxOverlayDetections
                            ? dets.OrderByDescending(d => d.Kind == "Entity").ThenByDescending(d => d.Score).Take(MaxOverlayDetections).ToList()
                            : dets;
                        _overlay.SetDetections(overlayDets, capW, capH);
                        PushWalkBoxOverlay();
                        _overlay.SetValues(rowData.Select(r => r.role + " = " + r.val).ToList(), OverlayValuesSize);
                    }
                }
            });
        }
        catch { }
        finally
        {
            _tickEmaMs = Ema(_tickEmaMs, Environment.TickCount64 - tickStarted, 0.22);
            _busy = false;
            ScheduleNextOcrTick();
        }
    }

    /// <summary>Commit a value only after it reads the same twice in a row; kills OCR flicker.</summary>
    private void ReplaceRawEntityBoxesWithTracks(List<FourRVivi.App.Overlay.DetBox> dets)
    {
        int removedRaw = dets.RemoveAll(d => d.Kind == "Entity" || d.Kind == "EntityHp");
        var tracked = FourRVivi.Core.Game.LiveScene.Instance.Entities;
        if (tracked.Count == 0)
        {
            FourRVivi.Core.Common.DebugTrace.Write("OCR", $"Overlay entity sync rawRemoved={removedRaw} fromTracks=0 liveAttackable=0 filterVersion={FourRVivi.Core.Game.LiveScene.Instance.FilterVersion}.");
            return;
        }

        int added = 0;
        foreach (var item in tracked.Where(e => e.State == FourRVivi.Core.Game.SceneTrackState.Visible && e.Misses == 0 && e.Confirmed))
        {
            int x = item.X;
            int y = item.Y;
            if (UseMonitor)
            {
                var mon = SelectedMonitor;
                var hwnd = _session.WindowHandle;
                if (mon == null || hwnd == IntPtr.Zero)
                    continue;
                var screen = new POINT { X = item.X, Y = item.Y };
                if (!ClientToScreen(hwnd, ref screen))
                    continue;
                x = screen.X - mon.X;
                y = screen.Y - mon.Y;
            }

            var state = item.IsAttackable ? "" : " not ready";
            var label = item.TrackId > 0 ? $"#{item.TrackId} {item.Label}{state}" : $"{item.Label}{state}";
            if (item.HasHp)
                label += $"  HP {Math.Clamp((int)Math.Round(item.HpRatio * 100), 1, 100)}%";
            dets.Add(new FourRVivi.App.Overlay.DetBox("Entity", x, y, item.W, item.H, label, item.Score));
            added++;
        }
        int attackable = tracked.Count(e => e.IsAttackable);
        int visible = tracked.Count(e => e.State == FourRVivi.Core.Game.SceneTrackState.Visible && e.Misses == 0);
        int lost = tracked.Count(e => e.IsLostGrace || e.Misses > 0);
        FourRVivi.Core.Common.DebugTrace.Write("OCR", $"Overlay entity sync rawRemoved={removedRaw} drawn={added} liveTracks={tracked.Count} visible={visible} lostHidden={lost} liveAttackable={attackable} filterVersion={FourRVivi.Core.Game.LiveScene.Instance.FilterVersion}.");
    }

    private void LogEntityScan(int rawCount, int entityCount, int hpCount, long elapsedMs, bool safeForBot, bool clientCoords, int capW, int capH, bool published, int sourceGrf, int sourceYolo, int sourceOther, bool grfBotOnlyScan = false)
    {
        long now = Environment.TickCount64;
        int throttleMs = published ? 250 : 1000;
        if (now - _lastEntityDiagMs < throttleMs)
            return;
        _lastEntityDiagMs = now;

        string mode = UseMonitor
            ? $"monitor index={SelectedMonitor?.Index.ToString() ?? "?"} dxgi={UseDxgiCapture}"
            : "window client";
        var stats = _ocr.LastEntityFilterStats;
        var tracker = FourRVivi.Core.Game.LiveScene.Instance.TrackerDiagnostics;
        var scene = FourRVivi.Core.Game.LiveScene.Instance.Entities;
        int visible = scene.Count(e => e.State == FourRVivi.Core.Game.SceneTrackState.Visible);
        int confirmed = scene.Count(e => e.Confirmed);
        int attackable = scene.Count(e => e.IsAttackable);
        string sceneSample = string.Join("; ", scene.Take(8).Select(e => $"#{e.TrackId}:{e.Label} s={e.Score:0.00} hit={e.Hits} miss={e.Misses} state={e.State} conf={e.Confirmed} atk={e.IsAttackable} box={e.X},{e.Y},{e.W}x{e.H}"));
        FourRVivi.Core.Common.DebugTrace.Write("OCR",
            $"Entity scan mode={mode} frame={capW}x{capH} raw={rawCount} entities={scene.Count} rawSurvivors={rawCount} acceptedBeforeTracker={entityCount} hpBars={hpCount} elapsedMs={elapsedMs} safeForBot={safeForBot} clientCoords={clientCoords} published={published} minScore={EntityMinScore:0.00} otherMin={_ocr.OtherEntityMinScore:0.00} detectMonsters={DetectMonsters} grfBotOnlyScan={grfBotOnlyScan} " +
            $"sourceGrf={sourceGrf} sourceYolo={sourceYolo} sourceOther={sourceOther} " +
            $"filterRaw={stats.Raw} monsterCand={stats.MonsterCandidates} otherCand={stats.OtherCandidates} hpCand={stats.HpCandidates} acceptedEntity={stats.AcceptedEntities} acceptedHp={stats.AcceptedHpBars} rejectConf={stats.RejectedByConfidence} rejectExcl={stats.RejectedByExclusion} rejectPlayer={stats.RejectedPlayerOrPlayerHp} rejectClass={stats.RejectedByClass} mapFocusGeneric={stats.RejectedByMapFocus} " +
            $"trackerInput={tracker.Input} high={tracker.High} low={tracker.Low} new={tracker.NewTracks} matchedHigh={tracker.MatchedHigh} matchedLow={tracker.MatchedLow} missed={tracker.Missed} removed={tracker.Removed} trackerActive={tracker.Active} lostGrace={tracker.LostGrace} " +
            $"tracks={scene.Count} visible={visible} confirmed={confirmed} attackable={attackable} rawSample=[{stats.RawSample}] acceptedSample=[{stats.AcceptedSample}] rejectedSample=[{stats.RejectedSample}] sceneSample=[{sceneSample}].");
    }

    private bool TryBuildSceneItem(OcrService.ScanFind fnd, out FourRVivi.Core.Game.SceneItem item)
    {
        item = new FourRVivi.Core.Game.SceneItem(0, 0, 0, 0, "", 0);
        bool grfSource = string.Equals(fnd.Source, "grf", StringComparison.OrdinalIgnoreCase);
        if (!UseMonitor)
        {
            int trackId = grfSource && fnd.MobId > 0 ? StableGrfTrackId(fnd) : 0;
            item = new FourRVivi.Core.Game.SceneItem(fnd.X, fnd.Y, fnd.W, fnd.H, fnd.Value, fnd.Score,
                TrackId: trackId,
                Hits: grfSource ? FourRVivi.Core.Game.LiveScene.TrackMinHits : 0,
                Confirmed: grfSource);
            return true;
        }

        var mon = SelectedMonitor;
        var hwnd = _session.WindowHandle;
        if (mon == null || hwnd == IntPtr.Zero) return false;

        var center = new POINT { X = mon.X + fnd.X + fnd.W / 2, Y = mon.Y + fnd.Y + fnd.H / 2 };
        if (!ScreenToClient(hwnd, ref center)) return false;
        if (!GetClientRect(hwnd, out var rect)) return false;
        int clientW = rect.Right - rect.Left;
        int clientH = rect.Bottom - rect.Top;
        if (center.X < 0 || center.Y < 0 || center.X >= clientW || center.Y >= clientH) return false;

        int x = Math.Max(0, center.X - fnd.W / 2);
        int y = Math.Max(0, center.Y - fnd.H / 2);
        int monitorTrackId = grfSource && fnd.MobId > 0 ? StableGrfTrackId(fnd, x, y) : 0;
        item = new FourRVivi.Core.Game.SceneItem(x, y, fnd.W, fnd.H, fnd.Value, fnd.Score,
            TrackId: monitorTrackId,
            Hits: grfSource ? FourRVivi.Core.Game.LiveScene.TrackMinHits : 0,
            Confirmed: grfSource);
        return true;
    }

    private int SmoothBarPercent(string role, int pct)
    {
        role = string.IsNullOrWhiteSpace(role) ? "bar" : role.Trim();
        if (!_barSamples.TryGetValue(role, out var samples))
        {
            samples = new Queue<int>();
            _barSamples[role] = samples;
        }

        samples.Enqueue(Math.Clamp(pct, 0, 100));
        while (samples.Count > 5)
            samples.Dequeue();

        var ordered = samples.OrderBy(x => x).ToArray();
        return ordered[ordered.Length / 2];
    }

    private bool ShouldPublishPercent(string role, int pct, double conf)
    {
        pct = Math.Clamp(pct, 0, 100);
        if (LiveStats.Instance.TryGetNumberMeta(role, out var previous) &&
            previous.Quality == LiveStatQuality.Trusted &&
            previous.Value - pct > 25 &&
            pct > 1 &&
            conf < 0.98)
        {
            if (_percentPending.TryGetValue(role, out var pending) && Math.Abs(pending.value - pct) <= 3)
                _percentPending[role] = (pct, pending.count + 1);
            else
                _percentPending[role] = (pct, 1);

            return _percentPending[role].count >= 2;
        }

        _percentPending.Remove(role);
        return true;
    }

    private static int StableGrfTrackId(OcrService.ScanFind fnd)
        => StableGrfTrackId(fnd, fnd.X, fnd.Y);

    private static int StableGrfTrackId(OcrService.ScanFind fnd, int x, int y)
    {
        if (fnd.MobId <= 0)
            return 0;
        return Math.Abs(HashCode.Combine(fnd.MobId, x / 24, y / 24)) + 1;
    }

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
