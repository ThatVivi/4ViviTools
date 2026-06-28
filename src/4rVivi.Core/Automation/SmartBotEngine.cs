using System.Linq;
using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Input-level auto-combat driven by vision (YOLO entities) + OCR stats. Per-monster rules
/// (attack/skill), a roam box with anti-stuck teleport, weapon/ammo equip, ammo tracking, OCR-driven
/// auto-reconnect, and a full activity log. Degrades gracefully when roles/vision are unavailable.</summary>
public sealed class SmartBotEngine : AutomationEngine
{
    // --- combat / keys ---
    public string AttackKey { get; set; } = "F1";
    public List<string> SkillRotation { get; } = new();   // global skills woven in
    public string LootKey { get; set; } = "Z";
    public string TeleportKey { get; set; } = "F12";       // fly wing / teleport hotkey
    public string ReturnKey { get; set; } = "F11";         // butterfly wing / town macro
    public int FleeAtHpPercent { get; set; } = 25;
    public int StuckSeconds { get; set; } = 8;             // no progress -> teleport
    public int ReturnAtWeightPercent { get; set; } = 90;
    public int RotationMs { get; set; } = 350;
    public int MoveWaitMs { get; set; } = 1000;   // after a walk-click: time for the character to arrive AND the OCR to re-read
    public bool ClickToMove { get; set; } = true;
    public bool ClickAttack { get; set; } = true;
    public int MoveRadius { get; set; } = 180;
    public bool UseVision { get; set; } = true;
    public bool HardwareClick { get; set; } = true;   // RO/DirectInput + Gepard ignore PostMessage clicks -> use real cursor

    // --- per-monster + gear ---
    public List<MonsterRule> Monsters { get; } = new();
    public string WeaponKey { get; set; } = "";            // equip-weapon hotkey
    public string AmmoKey { get; set; } = "";              // equip-ammo hotkey
    public bool EquipOnStart { get; set; } = false;
    public string AmmoRole { get; set; } = Roles.Ammo;     // LiveStats role holding ammo count
    public int StopAtAmmo { get; set; } = 0;               // halt attacking when ammo <= this (0 = ignore)

    // --- roam box (client px). When valid + UseWalkBox, the bot only wanders inside it. ---
    public bool UseWalkBox { get; set; } = false;
    public int BoxX { get; set; }
    public int BoxY { get; set; }
    public int BoxW { get; set; }
    public int BoxH { get; set; }

    // --- auto-reconnect (OCR-detected) ---
    public bool AutoReconnect { get; set; } = false;
    public List<string> ReconnectKeys { get; } = new();    // key sequence pressed on detection
    public List<string> ReconnectWords { get; } = new() { "disconnect", "reconnect", "lost connection", "select character", "to login" };

    public string TargetMap { get; set; } = "";    // only farm when the OCR-read MapName matches this
    public string AmmoName { get; set; } = "";      // chosen ammo name (so OCR/ammo tracking knows what to look for)
    public string AttackSkill { get; set; } = "";   // chosen attack-skill name (OCR reference)

    public int Kills { get; private set; }
    public DateTime StartedAt { get; private set; } = DateTime.Now;

    private readonly StatReader _stat;
    private readonly MouseSender _mouse = new();
    private readonly Random _rng = new();
    private readonly Dictionary<string, long> _skillCdUntil = new(StringComparer.OrdinalIgnoreCase);
    private int _skillIdx;
    private long _lastChangeTick, _lastMoveLogTick, _lastReconnectTick;
    private int _lastExp = -1, _lastHp = -1, _lastPx = -1, _lastPy = -1, _lastWeight = -1, _lastAmmo = -1;

    public SmartBotEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Smart Bot", s, k, t)
        => _stat = new StatReader(s);

    public override void ClearKeys()
    {
        AttackKey = LootKey = TeleportKey = ReturnKey = WeaponKey = AmmoKey = "";
        SkillRotation.Clear(); ReconnectKeys.Clear();
        foreach (var m in Monsters) m.SkillKey = "";
    }

    private void Log(BotLogKind kind, string text) { Report(text); BotLog.Instance.Add(kind, text); }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        StartedAt = DateTime.Now; Kills = 0; _lastChangeTick = Environment.TickCount64;
        if (EquipOnStart) EquipGear();

        while (!ct.IsCancellationRequested)
        {
            if (Enabled && (Session.Reader.Attached || LiveStats.Instance.IsFresh))
            {
                // 0) auto-reconnect: if the OCR scene shows a disconnect/login screen, run the sequence.
                if (AutoReconnect && DetectDisconnect() &&
                    Environment.TickCount64 - _lastReconnectTick > 15000)
                {
                    _lastReconnectTick = Environment.TickCount64;
                    Log(BotLogKind.Reconnect, "Disconnect detected — running reconnect sequence.");
                    foreach (var key in ReconnectKeys)
                    {
                        Keys.Tap(Hwnd, KeyName.ToVk(key), 30);
                        await Timing.DelayAsync(1200, ct);
                    }
                    await Timing.DelayAsync(3000, ct);
                    continue;
                }

                // Map gate: only act while the OCR MapName matches the picked map (internal OR display name).
                if (!string.IsNullOrWhiteSpace(TargetMap))
                {
                    var curMap = LiveStats.Instance.GetText("MapName");
                    if (!string.IsNullOrEmpty(curMap))
                    {
                        var disp = MapDisplay(TargetMap);
                        bool match = curMap.IndexOf(TargetMap, StringComparison.OrdinalIgnoreCase) >= 0
                                  || (!string.IsNullOrEmpty(disp) && curMap.IndexOf(disp, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!match) { await Timing.DelayAsync(1500, ct); continue; }
                    }
                }

                double hp = _stat.HpPercent;
                double wt = _stat.WeightPercent;

                if (wt >= 0 && wt >= ReturnAtWeightPercent)
                {
                    Log(BotLogKind.Info, $"Weight {wt:0}% — returning to town.");
                    Keys.Tap(Hwnd, KeyName.ToVk(ReturnKey), 20);
                    await Timing.DelayAsync(4000, ct);
                    continue;
                }

                if (hp >= 0 && hp <= FleeAtHpPercent)
                {
                    await Timing.DelayAsync(400, ct);
                    MaybeUnstuck(ct);
                    continue;
                }

                // ammo gate
                int ammo = ReadAmmo();
                bool ammoOk = !(StopAtAmmo > 0 && ammo >= 0 && ammo <= StopAtAmmo);
                if (!ammoOk)
                {
                    Log(BotLogKind.Ammo, $"Ammo {ammo} <= {StopAtAmmo} — holding fire.");
                    await Timing.DelayAsync(1500, ct);
                    continue;
                }

                bool visionActed = false;
                var (cw, ch) = _mouse.ClientSize(Hwnd);

                if (UseVision && cw > 0 && ch > 0 &&
                    LiveScene.Instance.IsFresh && LiveScene.Instance.ClientCoords)
                {
                    var pred = BuildTargetPredicate();
                    var tgt = LiveScene.Instance.Nearest(cw / 2, ch / 2, pred);
                    if (tgt is { } t)
                    {
                        int tx = Math.Clamp(t.Cx, 4, cw - 4), ty = Math.Clamp(t.Cy, 4, ch - 4);
                        // RO attack model: a skill is cast by pressing its hotkey FIRST, then clicking the
                        // target. With no skill assigned, a plain click is a normal (auto) attack.
                        string skillKey = PerMonsterSkillKey(t.Label);
                        long now = Environment.TickCount64;
                        bool skillReady = !string.IsNullOrEmpty(skillKey)
                            && !(_skillCdUntil.TryGetValue(skillKey, out var until) && now < until);
                        if (skillReady)
                        {
                            Keys.Tap(Hwnd, KeyName.ToVk(skillKey), 15);   // arm the skill
                            await Timing.DelayAsync(45, ct);
                            ClickAt(tx, ty);                               // then click the target
                            var rule = Monsters.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Name)
                                && t.Label.IndexOf(m.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                            _skillCdUntil[skillKey] = now + Math.Max(200, rule?.SkillCooldownMs ?? 300);
                            Log(BotLogKind.Skill, $"Skill {skillKey} on {t.Label} @ {tx},{ty}");
                        }
                        else
                        {
                            ClickAt(tx, ty);                               // normal attack
                            Log(BotLogKind.Movement, $"Attack {t.Label} ({t.Score:0.00}) @ {tx},{ty}");
                        }
                        // weave the global skill rotation, then loot
                        if (SkillRotation.Count > 0)
                        {
                            await Timing.DelayAsync(RotationMs / 3, ct);
                            var sk = SkillRotation[_skillIdx++ % SkillRotation.Count];
                            Keys.Tap(Hwnd, KeyName.ToVk(sk), 15);
                            Log(BotLogKind.Skill, $"Skill {sk} (rotation)");
                        }
                        await Timing.DelayAsync(RotationMs / 3, ct);
                        Keys.Tap(Hwnd, KeyName.ToVk(LootKey), 15);
                        visionActed = true;
                        await Timing.DelayAsync(RotationMs, ct);   // let the hit land + the OCR re-read
                    }
                }

                if (!visionActed && ClickToMove && cw > 0 && ch > 0)
                {
                    // Nothing to fight in view: click a roam point to WALK there, then WAIT for the character
                    // to actually travel AND for the OCR to capture the new area before deciding again.
                    // (A click only issues a move order; walking + a screen read take time. Re-clicking before
                    // that finishes is why the character never seemed to move.)
                    var (x, y) = RoamPoint(cw, ch);
                    ClickAt(x, y);
                    if (Environment.TickCount64 - _lastMoveLogTick > 2000)
                    { _lastMoveLogTick = Environment.TickCount64; Log(BotLogKind.Movement, $"Walk -> {x},{y} (waiting to arrive)"); }
                    await WaitUntilArrivedAsync(ct);
                }

                TrackProgressAndUnstuck(ct);
            }
            await Timing.DelayAsync(RotationMs, ct);
        }
    }

    private void EquipGear()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(WeaponKey)) { Keys.Tap(Hwnd, KeyName.ToVk(WeaponKey), 20); Log(BotLogKind.Info, $"Equip weapon ({WeaponKey})"); }
            if (!string.IsNullOrWhiteSpace(AmmoKey)) { Keys.Tap(Hwnd, KeyName.ToVk(AmmoKey), 20); Log(BotLogKind.Info, $"Equip ammo ({AmmoKey})"); }
        }
        catch { }
    }

    /// <summary>Target the configured attack-monsters by name; if none configured, any mob-looking label.</summary>
    private Func<string, bool> BuildTargetPredicate()
    {
        var names = Monsters.Where(m => m.Attack && !string.IsNullOrWhiteSpace(m.Name))
                            .Select(m => m.Name).ToList();
        var avoid = Monsters.Where(m => !m.Attack && !string.IsNullOrWhiteSpace(m.Name))
                            .Select(m => m.Name).ToList();
        if (names.Count > 0)
            return lbl => !string.IsNullOrEmpty(lbl)
                && names.Any(n => lbl.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                && !avoid.Any(n => lbl.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        return lbl => IsMonster(lbl) && !avoid.Any(n => lbl.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>The skill hotkey to cast on a given monster label (from its rule), or "" for a normal
    /// attack. Falls back to the global AttackSkill key if a monster has no specific skill.</summary>
    private string PerMonsterSkillKey(string label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        var rule = Monsters.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.SkillKey)
            && !string.IsNullOrWhiteSpace(m.Name)
            && label.IndexOf(m.Name, StringComparison.OrdinalIgnoreCase) >= 0);
        return rule?.SkillKey ?? "";
    }

    private (int x, int y) RoamPoint(int cw, int ch)
    {
        if (UseWalkBox && BoxW > 2 && BoxH > 2)
        {
            int x = Math.Clamp(BoxX + _rng.Next(0, BoxW), 4, cw - 4);
            int y = Math.Clamp(BoxY + _rng.Next(0, BoxH), 4, ch - 4);
            return (x, y);
        }
        return (Math.Clamp(cw / 2 + _rng.Next(-MoveRadius, MoveRadius), 4, cw - 4),
                Math.Clamp(ch / 2 + _rng.Next(-MoveRadius, MoveRadius), 4, ch - 4));
    }

    private bool DetectDisconnect()
    {
        foreach (var w in ReconnectWords)
            if (LiveScene.Instance.HasStatus(w)) return true;
        return false;
    }

    private int ReadAmmo()
        => LiveStats.Instance.TryGetNumber(AmmoRole, out var v) ? v : -1;

    private static System.Collections.Generic.Dictionary<string, string>? _mapDisplay;
    private static string MapDisplay(string internalName)
    {
        if (_mapDisplay == null)
        {
            _mapDisplay = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string bd = AppContext.BaseDirectory;
                foreach (var d in new[]
                {
                    System.IO.Path.Combine(bd, "OcrServer", "models", "icons"),
                    System.IO.Path.Combine(bd, "models", "icons"),
                })
                {
                    var jf = System.IO.Path.Combine(d, "map_names.json");
                    if (!System.IO.File.Exists(jf)) continue;
                    using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(jf));
                    foreach (var prop in doc.RootElement.EnumerateObject()) _mapDisplay[prop.Name] = prop.Value.GetString() ?? "";
                    break;
                }
            }
            catch { }
        }
        return _mapDisplay.TryGetValue(internalName, out var v) ? v : "";
    }

    private static bool IsMonster(string label)
        => !string.IsNullOrEmpty(label) &&
           (label.IndexOf("monster", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("mob", StringComparison.OrdinalIgnoreCase) >= 0);

    private void ClickAt(int x, int y) => _mouse.Click(Hwnd, x, y);   // backend chosen by the Input method selector

    private void TrackProgressAndUnstuck(CancellationToken ct)
    {
        int exp = _stat.Exp, hp = _stat.Hp, px = _stat.PosX, py = _stat.PosY;
        int weight = _stat.Weight, ammo = ReadAmmo();
        bool changed =
            (exp >= 0 && exp != _lastExp) ||
            (hp >= 0 && hp != _lastHp) ||
            (px >= 0 && (px != _lastPx || py != _lastPy));
        if (exp > _lastExp && _lastExp >= 0) { Kills++; Log(BotLogKind.Kill, $"Kill #{Kills} (EXP {_lastExp} -> {exp})"); }
        if (weight >= 0 && _lastWeight >= 0 && weight > _lastWeight) Log(BotLogKind.Item, $"Item picked (weight {_lastWeight} -> {weight})");
        if (ammo >= 0 && _lastAmmo >= 0 && ammo != _lastAmmo) Log(BotLogKind.Ammo, $"Ammo left: {ammo}");
        if (changed) _lastChangeTick = Environment.TickCount64;
        _lastExp = exp; _lastHp = hp; _lastPx = px; _lastPy = py;
        if (weight >= 0) _lastWeight = weight;
        if (ammo >= 0) _lastAmmo = ammo;
        MaybeUnstuck(ct);
    }

    /// <summary>After a walk order, watch the OCR position (PosX/PosY) and return once it stops changing for a
    /// short spell (arrived) — or after MoveWaitMs at most. This is how the bot reads moving vs stopped from
    /// the numbers, now that the Character motion box is hidden.</summary>
    private async Task WaitUntilArrivedAsync(CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + Math.Max(400, MoveWaitMs);
        int lastX = _stat.PosX, lastY = _stat.PosY;
        long stableSince = Environment.TickCount64;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            await Timing.DelayAsync(120, ct);
            int x = _stat.PosX, y = _stat.PosY;
            if (x < 0 || y < 0) continue;                       // no position read -> use the time cap
            if (x != lastX || y != lastY)
            { lastX = x; lastY = y; stableSince = Environment.TickCount64; }   // still moving
            else if (Environment.TickCount64 - stableSince >= 400)
                return;                                          // held 400ms -> arrived / stopped
        }
    }

    private void MaybeUnstuck(CancellationToken ct)
    {
        if (Environment.TickCount64 - _lastChangeTick < StuckSeconds * 1000L) return;
        Log(BotLogKind.Movement, $"No progress for {StuckSeconds}s — teleporting.");
        Keys.Tap(Hwnd, KeyName.ToVk(TeleportKey), 20);
        _lastChangeTick = Environment.TickCount64;
    }
}
