using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.App.Services;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Ocr;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

public sealed partial class MultiClientRow : ObservableObject
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public IntPtr WindowHandle { get; init; }
    public string Display => $"{Name}.exe ({Pid}) - {WindowTitle}";

    [ObservableProperty] private bool _ocrEnabled;
    [ObservableProperty] private bool _activeTarget;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _hp = "-";
    [ObservableProperty] private string _sp = "-";
    [ObservableProperty] private string _map = "-";
    [ObservableProperty] private string _entities = "-";
    [ObservableProperty] private bool _botEnabled;
    [ObservableProperty] private string _purpose = "Main";
    [ObservableProperty] private string _skillKey = "";
    [ObservableProperty] private string _buffKey = "";
    [ObservableProperty] private bool _autoBuff;
    [ObservableProperty] private int _buffIntervalSec = 120;
    [ObservableProperty] private string _lastTarget = "-";
    [ObservableProperty] private int _clicks;
    [ObservableProperty] private int _buffs;
    [ObservableProperty] private string _lastFrame = "-";
    [ObservableProperty] private DateTime _updated = DateTime.MinValue;
    public DateTime LastInputUtc { get; set; } = DateTime.MinValue;
    public DateTime LastBuffUtc { get; set; } = DateTime.MinValue;
}

/// <summary>Runs OCR over several attached client windows without requiring them to be focused.</summary>
public sealed partial class MultiClientViewModel : ViewModelBase, IDisposable
{
    private readonly ProcessService _processes;
    private readonly SettingsStore _settings;
    private readonly ConcurrentDictionary<int, OcrService> _ocrByPid = new();
    private readonly MouseSender _mouse = new() { Method = InputMethod.PostMessage };
    private readonly KeySender _keys = new() { Method = InputMethod.PostMessage };
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _ocrGate = new(4, 4);

    public ObservableCollection<GameProcess> Processes { get; } = new();
    public ObservableCollection<MultiClientRow> Clients { get; } = new();
    public string[] ClientPurposes { get; } = { "Main", "Buffer", "Watcher" };

    [ObservableProperty] private GameProcess? _selectedProcess;
    [ObservableProperty] private int _intervalMs = 650;
    [ObservableProperty] private int _inputDelayMs = 750;
    [ObservableProperty] private int _defaultBuffIntervalSec = 120;
    [ObservableProperty] private bool _running;
    [ObservableProperty] private string _status = "Add running RO clients, then Start OCR for all.";

    public MultiClientViewModel(ProcessService processes, SettingsStore settings)
    {
        _processes = processes;
        _settings = settings;
        RefreshProcesses();
    }

    private OcrService ClientOcr(int pid)
        => _ocrByPid.GetOrAdd(pid, _ =>
        {
            var ocr = new OcrService
            {
                Tuning = _settings.Current.OcrTuning,
                EntityMinScore = (float)_settings.Current.OcrToggles.EntityMinScore,
                TextMinScore = (float)_settings.Current.OcrToggles.TextMinScore,
                MultiPass = _settings.Current.OcrToggles.MultiPass,
                NameEntitiesByText = _settings.Current.OcrToggles.GrfNamesAbove,
                NameEntitiesByIcon = !_settings.Current.OcrToggles.GrfNamesAbove,
                VisionAssistGrf = _settings.Current.OcrToggles.VisionAssistGrf,
                VisionAssistManifestPath = "",
            };
            ocr.ApplyTuning();
            return ocr;
        });

    [RelayCommand]
    private void RefreshProcesses()
    {
        Processes.Clear();
        var prefer = _settings.Current.GetActiveProfile().PreferredProcessNames;
        foreach (var p in _processes.List(prefer))
            Processes.Add(p);
        Status = $"Found {Processes.Count} window(s).";
    }

    [RelayCommand]
    private void AddSelectedClient()
    {
        if (SelectedProcess == null)
        {
            Status = "Pick a client window first.";
            return;
        }

        if (Clients.Any(c => c.Pid == SelectedProcess.Pid))
        {
            Status = "That client is already in the list.";
            return;
        }

        Clients.Add(new MultiClientRow
        {
            Pid = SelectedProcess.Pid,
            Name = SelectedProcess.Name,
            WindowTitle = SelectedProcess.WindowTitle,
            WindowHandle = SelectedProcess.WindowHandle,
            OcrEnabled = true,
            BuffIntervalSec = Math.Clamp(DefaultBuffIntervalSec, 5, 3600),
            Status = "Added",
        });
        Status = $"Added {SelectedProcess.Display}.";
    }

    [RelayCommand]
    private void RemoveClient(MultiClientRow? row)
    {
        if (row == null) return;
        Clients.Remove(row);
        if (_ocrByPid.TryRemove(row.Pid, out var ocr))
            ocr.Dispose();
    }

    [RelayCommand]
    private void StartAll()
    {
        foreach (var c in Clients) c.OcrEnabled = true;
        StartLoop();
    }

    [RelayCommand]
    private void StopAll()
    {
        _cts?.Cancel();
        _cts = null;
        Running = false;
        foreach (var c in Clients) c.OcrEnabled = false;
        Status = "Multi-client OCR stopped.";
    }

    [RelayCommand]
    private void StartClient(MultiClientRow? row)
    {
        if (row == null) return;
        row.OcrEnabled = true;
        StartLoop();
    }

    [RelayCommand]
    private void StopClient(MultiClientRow? row)
    {
        if (row == null) return;
        row.OcrEnabled = false;
        row.Status = "Stopped";
        if (!Clients.Any(c => c.OcrEnabled)) StopAll();
    }

    [RelayCommand]
    private void StartBots()
    {
        foreach (var c in Clients)
        {
            c.OcrEnabled = true;
            c.BotEnabled = true;
            if (c.BuffIntervalSec <= 0) c.BuffIntervalSec = Math.Clamp(DefaultBuffIntervalSec, 5, 3600);
        }
        StartLoop();
        Status = "Multi-client OCR + unfocused PostMessage input running.";
    }

    [RelayCommand]
    private void StopBots()
    {
        foreach (var c in Clients)
            c.BotEnabled = false;
        Status = "Multi-client bot input stopped. OCR can keep running.";
    }

    [RelayCommand]
    private void BuffNow(MultiClientRow? row)
    {
        if (row == null) return;
        SendBuffInput(row, force: true);
    }

    [RelayCommand]
    private void MakeActive(MultiClientRow? row)
    {
        if (row == null) return;
        var gp = new GameProcess(row.Pid, row.Name, row.WindowTitle, row.WindowHandle);
        var result = _processes.Attach(gp);
        foreach (var c in Clients) c.ActiveTarget = ReferenceEquals(c, row);
        row.Status = result.Ok ? "Active Smart Bot target" : result.Error ?? "Attach failed";
        Status = result.Ok ? $"Active client is now {row.Display}." : row.Status;
    }

    private void StartLoop()
    {
        if (Running) return;
        if (Clients.Count == 0)
        {
            Status = "Add at least one client first.";
            return;
        }

        _cts = new CancellationTokenSource();
        Running = true;
        Status = "Multi-client OCR running. Clients do not need focus or to be on top.";
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var enabled = await Dispatcher.UIThread.InvokeAsync(() => Clients.Where(c => c.OcrEnabled).ToList());
            if (enabled.Count > 0)
                await Task.WhenAll(enabled.Select(client => ReadClientAsync(client, ct)));

            try { await Task.Delay(Math.Clamp(IntervalMs, 250, 5000), ct); }
            catch (TaskCanceledException) { }
        }
    }

    private async Task ReadClientAsync(MultiClientRow client, CancellationToken ct)
    {
        await _ocrGate.WaitAsync(ct);
        try
        {
            if (!ProcessStillRunning(client.Pid))
            {
                Post(client, c => c.Status = "Process closed");
                return;
            }

            var ocr = ClientOcr(client.Pid);
            using var frame = ocr.CaptureWindow(client.WindowHandle);
            if (frame == null)
            {
                Post(client, c => c.Status = "No frame. If minimized, restore once; some clients stop rendering while minimized.");
                return;
            }

            string hp = "-", sp = "-", map = "-";
            foreach (var mark in _settings.Current.OcrMarks.ToList())
            {
                if (ct.IsCancellationRequested) return;
                ReadMark(ocr, frame, mark, ref hp, ref sp, ref map);
            }

            var entities = new List<OcrService.ScanFind>();
            try
            {
                ocr.ScanTextEnabled = false;
                ocr.ScanEntitiesEnabled = true;
                entities = ocr.ScanEntitiesOnly(frame).Where(x => x.Kind == "Entity").ToList();
            }
            catch { }

            var target = PickTarget(entities, frame.Width, frame.Height);
            if (client.BotEnabled)
            {
                if (IsBuffer(client))
                    MaybeSendBuffInput(client);
                else if (target != null && IsMainOrFighter(client))
                    SendUnfocusedInput(client, target, ct);
            }

            string size = $"{frame.Width}x{frame.Height}";
            string targetText = target == null ? "-" : $"{target.Value} @ {target.Cx},{target.Cy}";
            Post(client, c =>
            {
                c.Hp = hp;
                c.Sp = sp;
                c.Map = map;
                c.Entities = entities.Count.ToString();
                c.LastTarget = targetText;
                c.LastFrame = size;
                c.Updated = DateTime.Now;
                c.Status = client.BotEnabled
                    ? (IsBuffer(client) ? "Hard attached OCR + buffer input" : "Hard attached OCR + unfocused input")
                    : "Hard attached OCR OK";
            });
        }
        catch (Exception ex)
        {
            Post(client, c => c.Status = "OCR error: " + ex.Message);
        }
        finally
        {
            _ocrGate.Release();
        }
    }

    private void ReadMark(OcrService ocr, System.Drawing.Bitmap frame, OcrMark mark, ref string hp, ref string sp, ref string map)
    {
        if (mark.IsChar || mark.IsIcons || string.IsNullOrWhiteSpace(mark.Role)) return;
        string role = NormalizeRole(mark.Role);

        if (mark.Role == "Weight / MaxWeight")
        {
            string raw = ReadText(ocr, frame, mark, numeric: true);
            var two = OcrService.ParseTwoInts(raw);
            if (two is not { } pair) return;
            return;
        }

        if (role == Roles.HpPercent)
        {
            var v = ocr.ReadPercentTextFrom(frame, mark.X, mark.Y, mark.W, mark.H, 0, 0, role, "Paddle", out _, out _, out _, out _);
            if (v >= 0) hp = $"{v}%";
            return;
        }

        if (role == Roles.SpPercent)
        {
            var v = ocr.ReadPercentTextFrom(frame, mark.X, mark.Y, mark.W, mark.H, 0, 0, role, "Paddle", out _, out _, out _, out _);
            if (v >= 0) sp = $"{v}%";
            return;
        }

        if (mark.Role == "MapName")
        {
            var txt = ReadText(ocr, frame, mark, numeric: false).Trim();
            if (txt.Length > 0) map = txt;
        }
    }

    private string ReadText(OcrService ocr, System.Drawing.Bitmap frame, OcrMark mark, bool numeric)
    {
        string preprocess = string.IsNullOrEmpty(mark.Preprocess) || mark.Preprocess == "Auto"
            ? ocr.SuggestPreprocess(mark.Role)
            : mark.Preprocess;
        string engine = string.IsNullOrWhiteSpace(mark.Engine) ? "Paddle" : mark.Engine;
        double scale = string.IsNullOrEmpty(mark.Preprocess) || mark.Preprocess == "Auto"
            ? ocr.SuggestScale(mark.Role)
            : 0;
        return ocr.ReadRectFrom(frame, mark.X, mark.Y, mark.W, mark.H, numeric, 0, 0, preprocess, mark.Sharpen, engine, scale);
    }

    private static string NormalizeRole(string role) => role switch
    {
        "HP % Text" or "HP Bar" => Roles.HpPercent,
        "SP % Text" or "SP Bar" => Roles.SpPercent,
        _ => role
    };

    private static OcrService.ScanFind? PickTarget(IReadOnlyList<OcrService.ScanFind> entities, int width, int height)
    {
        if (entities.Count == 0) return null;
        int cx = width / 2, cy = height / 2;
        OcrService.ScanFind? best = null;
        long bestD = long.MaxValue;
        foreach (var e in entities)
        {
            if (!LooksAttackable(e.Value)) continue;
            long dx = e.Cx - cx, dy = e.Cy - cy;
            long d = dx * dx + dy * dy;
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }
        return best;
    }

    private static bool LooksAttackable(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        return !label.Equals("loot", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("portal", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("player", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("target", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("target hp", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("player hp", StringComparison.OrdinalIgnoreCase);
    }

    private void SendUnfocusedInput(MultiClientRow client, OcrService.ScanFind target, CancellationToken ct)
    {
        if (client.WindowHandle == IntPtr.Zero || ct.IsCancellationRequested) return;
        if ((DateTime.UtcNow - client.LastInputUtc).TotalMilliseconds < Math.Clamp(InputDelayMs, 250, 5000)) return;
        client.LastInputUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(client.SkillKey))
        {
            _keys.Tap(client.WindowHandle, KeyName.ToVk(client.SkillKey), 25);
            Thread.Sleep(45);
        }

        _mouse.Click(client.WindowHandle, target.Cx, target.Cy);
        Post(client, c =>
        {
            c.Clicks++;
            c.Status = string.IsNullOrWhiteSpace(c.SkillKey)
                ? $"Clicked {target.Value} without focus"
                : $"Sent {c.SkillKey}, then clicked {target.Value} without focus";
        });
    }

    private static bool IsBuffer(MultiClientRow client)
        => string.Equals(client.Purpose, "Buffer", StringComparison.OrdinalIgnoreCase);

    private static bool IsMainOrFighter(MultiClientRow client)
        => !string.Equals(client.Purpose, "Watcher", StringComparison.OrdinalIgnoreCase)
        && !IsBuffer(client);

    private void MaybeSendBuffInput(MultiClientRow client)
    {
        if (!client.AutoBuff) return;
        int interval = Math.Clamp(client.BuffIntervalSec, 5, 3600);
        if ((DateTime.UtcNow - client.LastBuffUtc).TotalSeconds < interval) return;
        SendBuffInput(client, force: false);
    }

    private void SendBuffInput(MultiClientRow client, bool force)
    {
        if (client.WindowHandle == IntPtr.Zero) return;
        if (string.IsNullOrWhiteSpace(client.BuffKey))
        {
            Post(client, c => c.Status = "Set a buff key first.");
            return;
        }

        if (!force && !client.AutoBuff) return;
        client.LastBuffUtc = DateTime.UtcNow;
        _keys.Tap(client.WindowHandle, KeyName.ToVk(client.BuffKey), 35);
        Post(client, c =>
        {
            c.Buffs++;
            c.Status = $"Sent buff key {c.BuffKey} without focus";
        });
    }

    private static bool ProcessStillRunning(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    private static void Post(MultiClientRow row, Action<MultiClientRow> update)
        => Dispatcher.UIThread.Post(() => update(row));

    public void Dispose()
    {
        _cts?.Cancel();
        _ocrGate.Dispose();
        foreach (var ocr in _ocrByPid.Values)
            ocr.Dispose();
        _ocrByPid.Clear();
    }
}
