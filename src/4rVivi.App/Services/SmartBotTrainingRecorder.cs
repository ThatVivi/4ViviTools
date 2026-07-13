using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.Services;

public sealed class SmartBotTrainingRecorder : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    private readonly EngineHub _hub;
    private readonly SettingsStore _settings;
    private GlobalKeyHook? _keys;
    private readonly LowLevelMouseProc _mouseProc;
    private readonly object _lock = new();
    private readonly Stopwatch _clock = new();
    private readonly List<int> _skillConfirm = new();
    private readonly List<int> _normalAttack = new();
    private readonly List<int> _walkWait = new();
    private readonly List<int> _teleport = new();
    private readonly List<int> _potReaction = new();
    private readonly List<int> _potUse = new();
    private readonly Dictionary<string, long> _lastKeyDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _lastSameKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _skillKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _potKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _teleportKeys = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _mouseHook;
    private StreamWriter? _log;
    private Timer? _sampleTimer;
    private long _lastLeftClickTick;
    private long _lastAttackClickTick;
    private int _lastExp = -1;
    private bool _running;

    public event Action<string>? StatusChanged;

    public SmartBotTrainingRecorder(EngineHub hub, SettingsStore settings)
    {
        _hub = hub;
        _settings = settings;
        _mouseProc = MouseCallback;
    }

    public bool Running
    {
        get { lock (_lock) return _running; }
    }

    public string LogPath { get; private set; } = "";

    public void Start()
    {
        lock (_lock)
        {
            if (_running) return;
            RefreshKnownKeys();
            _clock.Restart();
            _skillConfirm.Clear();
            _normalAttack.Clear();
            _walkWait.Clear();
            _teleport.Clear();
            _potReaction.Clear();
            _potUse.Clear();
            _lastKeyDown.Clear();
            _lastSameKey.Clear();
            _lastExp = -1;
            _lastLeftClickTick = 0;
            _lastAttackClickTick = 0;
            SmartBotTrainingTuning.Instance.Reset();

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "Training");
            Directory.CreateDirectory(dir);
            LogPath = Path.Combine(dir, $"smart-bot-training-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            _log = new StreamWriter(File.Open(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            try
            {
                _keys?.Dispose();
                _keys = new GlobalKeyHook();
                _keys.KeyPressed += OnKeyPressed;
            }
            catch (Exception ex)
            {
                _keys = null;
                DebugTrace.Write("SmartBotTraining", "Keyboard training hook is unavailable; mouse, OCR, and scene samples will still be recorded.", ex);
            }
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero)
                DebugTrace.Write("SmartBotTraining", $"Mouse training hook failed. win32={Marshal.GetLastWin32Error()}");
            _sampleTimer = new Timer(_ => SampleScene(), null, 250, 250);
            _running = true;
            Write("session_start", new { LogPath });
        }
        PublishStatus();
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            _sampleTimer?.Dispose();
            _sampleTimer = null;
            CleanupHooksUnsafe();
            Write("session_stop", BuildSummaryUnsafe());
            _log?.Dispose();
            _log = null;
        }
        PublishStatus();
    }

    public string Summary
    {
        get { lock (_lock) return BuildSummaryTextUnsafe(); }
    }

    private void OnKeyPressed(int vk)
    {
        var key = KeyName.FromVk(vk);
        if (string.IsNullOrWhiteSpace(key)) return;

        lock (_lock)
        {
            if (!_running || !_knownKeys.Contains(key) || !IsTrainingForeground()) return;
            long now = _clock.ElapsedMilliseconds;
            _lastKeyDown[key] = now;
            if (_lastSameKey.TryGetValue(key, out var prev) && now > prev)
            {
                int gap = (int)(now - prev);
                if (_skillKeys.Contains(key) && gap is >= 120 and <= 5000) Add(_skillConfirm, gap);
                if (_potKeys.Contains(key) && gap is >= 120 and <= 6000) Add(_potUse, gap);
                if (_teleportKeys.Contains(key) && gap is >= 250 and <= 9000) Add(_teleport, gap);
            }
            _lastSameKey[key] = now;
            if (_potKeys.Contains(key))
                Add(_potReaction, EstimatePotReactionMs());
            Write("key", new { key, ms = now, hp = HpPercent(), sp = SpPercent(), map = LiveStats.Instance.GetText(Roles.MapName) });
            ApplyLearningUnsafe();
        }
        PublishStatus();
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                var pt = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
                OnMouseDown(msg == WM_LBUTTONDOWN ? "left" : "right", pt.x, pt.y);
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void OnMouseDown(string button, int x, int y)
    {
        lock (_lock)
        {
            if (!_running || !IsTrainingForeground()) return;
            long now = _clock.ElapsedMilliseconds;
            if (button == "left")
            {
                var skill = _lastKeyDown
                    .Where(kv => _skillKeys.Contains(kv.Key) && now - kv.Value is >= 0 and <= 1600)
                    .OrderByDescending(kv => kv.Value)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(skill.Key))
                {
                    Add(_skillConfirm, (int)(now - skill.Value));
                    _lastAttackClickTick = now;
                }
                else
                {
                    if (_lastLeftClickTick > 0)
                    {
                        int gap = (int)(now - _lastLeftClickTick);
                        if (gap is >= 90 and <= 900) Add(_normalAttack, gap);
                        if (gap is >= 450 and <= 5000) Add(_walkWait, gap);
                    }
                    _lastAttackClickTick = now;
                }
                _lastLeftClickTick = now;
            }
            Write("mouse", new { button, x, y, ms = now, entities = LiveScene.Instance.Entities.Count, hp = HpPercent(), sp = SpPercent() });
            ApplyLearningUnsafe();
        }
        PublishStatus();
    }

    private void SampleScene()
    {
        bool publish = false;
        lock (_lock)
        {
            if (!_running) return;
            int exp = LiveStats.Instance.TryGetNumber(Roles.Exp, out var expValue) ? expValue : -1;
            if (exp > _lastExp && _lastExp >= 0 && _lastAttackClickTick > 0)
            {
                int killMs = (int)Math.Clamp(_clock.ElapsedMilliseconds - _lastAttackClickTick, 120, 30000);
                Add(_skillConfirm, Math.Clamp(killMs / 2, 120, 5000));
                Write("kill_observed", new { expBefore = _lastExp, expAfter = exp, killMs });
            }
            if (exp >= 0) _lastExp = exp;

            if (_clock.ElapsedMilliseconds % 1000 < 300)
            {
                Write("scene", new
                {
                    ms = _clock.ElapsedMilliseconds,
                    map = LiveStats.Instance.GetText(Roles.MapName),
                    hp = HpPercent(),
                    sp = SpPercent(),
                    entities = LiveScene.Instance.Entities.Count,
                    sceneAgeMs = LiveScene.Instance.EntityUpdatedUtc == DateTime.MinValue ? -1 : (int)(DateTime.UtcNow - LiveScene.Instance.EntityUpdatedUtc).TotalMilliseconds,
                });
                publish = true;
            }
            ApplyLearningUnsafe();
        }
        if (publish) PublishStatus();
    }

    private void RefreshKnownKeys()
    {
        _knownKeys.Clear();
        _skillKeys.Clear();
        _potKeys.Clear();
        _teleportKeys.Clear();
        foreach (var k in KeyName.Common.Concat(new[] { "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z","Back", "Start", "LeftShoulder", "RightShoulder" }))
            _knownKeys.Add(k);

        var smart = _settings.Current.GetActiveProfile().SmartBot;
        foreach (var row in smart.SkillButtons.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
        {
            var key = row.Key.Trim();
            _knownKeys.Add(key);
            if (row.IsSkill) _skillKeys.Add(key);
            if (row.IsHpPot || row.IsSpPot || row.IsYgg) _potKeys.Add(key);
            if (row.IsTeleport) _teleportKeys.Add(key);
        }
        foreach (var pot in _settings.Current.GetActiveProfile().Pots.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
        {
            var key = pot.Key.Trim();
            _knownKeys.Add(key);
            _potKeys.Add(key);
        }
        if (!string.IsNullOrWhiteSpace(_hub.SmartBot.TeleportKey))
        {
            _knownKeys.Add(_hub.SmartBot.TeleportKey);
            _teleportKeys.Add(_hub.SmartBot.TeleportKey);
        }
    }

    private bool IsTrainingForeground()
    {
        var hwnd = _hub.Session.WindowHandle;
        return hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
    }

    private int EstimatePotReactionMs()
    {
        var hp = HpPercent();
        var sp = SpPercent();
        var urgency = Math.Max(0, 70 - Math.Min(hp < 0 ? 100 : hp, sp < 0 ? 100 : sp));
        return Math.Clamp((int)Math.Round(180 - urgency * 1.7), 20, 220);
    }

    private void ApplyLearningUnsafe()
    {
        SmartBotTrainingTuning.Instance.Update(
            skillDelayMs: Median(_skillConfirm),
            normalAttackDelayMs: Median(_normalAttack),
            walkWaitMs: Median(_walkWait),
            teleportDelayMs: Median(_teleport),
            potReactionMs: Median(_potReaction),
            potUseDelayMs: Median(_potUse),
            sampleCount: _skillConfirm.Count + _normalAttack.Count + _walkWait.Count + _teleport.Count + _potReaction.Count + _potUse.Count);
    }

    private static void Add(List<int> list, int value)
    {
        list.Add(value);
        if (list.Count > 200) list.RemoveAt(0);
    }

    private static int? Median(List<int> list)
    {
        if (list.Count < 2) return null;
        var values = list.OrderBy(x => x).ToArray();
        int trim = values.Length >= 10 ? values.Length / 10 : 0;
        var usable = values.Skip(trim).Take(values.Length - trim * 2).ToArray();
        if (usable.Length == 0) usable = values;
        return usable[usable.Length / 2];
    }

    private static double HpPercent()
        => LiveStats.Instance.TryGetTrustedNumber(Roles.HpPercent, out var p) ? p : -1;

    private static double SpPercent()
        => LiveStats.Instance.TryGetTrustedNumber(Roles.SpPercent, out var p) ? p : -1;

    private static double Percent(string role, string maxRole)
    {
        if (!LiveStats.Instance.TryGetNumber(role, out var value) ||
            !LiveStats.Instance.TryGetNumber(maxRole, out var max) ||
            max <= 0)
            return -1;
        return Math.Clamp(value * 100.0 / max, 0.0, 100.0);
    }

    private void Write(string type, object data)
    {
        try
        {
            _log?.WriteLine(JsonSerializer.Serialize(new { type, at = DateTimeOffset.Now, data }));
        }
        catch (Exception ex) { DebugTrace.Write("SmartBotTraining", "Failed to write training event.", ex); }
    }

    private object BuildSummaryUnsafe()
        => new
        {
            skillDelayMs = Median(_skillConfirm),
            normalAttackMs = Median(_normalAttack),
            walkWaitMs = Median(_walkWait),
            teleportDelayMs = Median(_teleport),
            potReactionMs = Median(_potReaction),
            potUseDelayMs = Median(_potUse),
            samples = _skillConfirm.Count + _normalAttack.Count + _walkWait.Count + _teleport.Count + _potReaction.Count + _potUse.Count,
        };

    private string BuildSummaryTextUnsafe()
    {
        var samples = _skillConfirm.Count + _normalAttack.Count + _walkWait.Count + _teleport.Count + _potReaction.Count + _potUse.Count;
        if (!_running && samples == 0) return "Smart Bot Training is idle.";
        var state = _running ? "Recording" : "Training stopped";
        return $"{state}: {samples} sample(s). Learned skill {Text(Median(_skillConfirm))}, attack {Text(Median(_normalAttack))}, walk {Text(Median(_walkWait))}, teleport {Text(Median(_teleport))}, pot {Text(Median(_potUse))}.";
    }

    private void PublishStatus() => StatusChanged?.Invoke(Summary);

    private static string Text(int? value) => value.HasValue ? $"{value.Value} ms" : "auto";

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            CleanupHooksUnsafe();
        }
    }

    private void CleanupHooksUnsafe()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keys != null)
        {
            _keys.KeyPressed -= OnKeyPressed;
            _keys.Dispose();
            _keys = null;
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)] private readonly struct POINT { public readonly int x; public readonly int y; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct MSLLHOOKSTRUCT { public readonly POINT pt; public readonly uint mouseData; public readonly uint flags; public readonly uint time; public readonly IntPtr dwExtraInfo; }
}
