using System.Linq;
using FourRVivi.Core.Common;
using FourRVivi.Core.Data;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Input-level auto-combat driven by vision (YOLO entities) + OCR stats, a roam box with anti-stuck
/// teleport, weapon/ammo equip, ammo tracking, OCR-driven auto-reconnect, and a full activity log.
/// Degrades gracefully when roles/vision are unavailable.</summary>
public sealed class SmartBotEngine : AutomationEngine
{
    // --- combat / keys ---
    public string AttackKey { get; set; } = "A";
    public List<string> SkillRotation { get; } = new();   // global skills woven in
    public List<string> BuffKeys { get; } = new();         // self-buffs kept up while the bot runs
    public string LootKey { get; set; } = "X";
    public string TeleportKey { get; set; } = "Back";       // fly wing / teleport hotkey
    public string ReturnKey { get; set; } = "Start";        // butterfly wing / town macro
    public int FleeAtHpPercent { get; set; } = 25;
    public int StuckMs { get; set; } = 8000;               // no progress -> teleport
    public int FocusKillMs { get; set; } = -1;             // -1 = auto from HP bar + learned damage + mob HP
    public int StuckSeconds { get => Math.Max(1, StuckMs / 1000); set => StuckMs = Math.Max(2, value) * 1000; }
    public int FocusKillSeconds { get => FocusKillMs < 0 ? -1 : Math.Max(1, (int)Math.Ceiling(FocusKillMs / 1000.0)); set => FocusKillMs = value < 0 ? -1 : Math.Clamp(value, 1, 600) * 1000; }
    public int NextMonsterDelayMs { get; set; } = -1;      // -1 = auto: scene/input cadence before choosing the next monster
    public int ReturnAtWeightPercent { get; set; } = 90;
    public int RotationMs { get; set; } = -1;
    public int BuffIntervalMs { get; set; } = 120000;
    public int MoveWaitMs { get; set; } = -1;   // -1 = auto: distance/capture/OCR based walking wait
    public int MoveStableMs { get; set; } = -1; // -1 = auto: stable-position window derived from MoveWaitMs
    public bool ClickToMove { get; set; } = true;
    public bool ClickAttack { get; set; } = true;
    public int MoveRadius { get; set; } = 180;
    public bool UseVision { get; set; } = true;
    public bool HardwareClick { get; set; } = true;   // RO/DirectInput + Gepard ignore PostMessage clicks -> use real cursor
    public bool UseControllerButtons { get; set; } = true;
    public Dictionary<string, string> ControllerKeyMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SkillSpRequired { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SkillDelayMsByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ActionDelayMsByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> BuffIntervalByKeyMs { get; } = new(StringComparer.OrdinalIgnoreCase);

    // --- legacy monster profile rows + gear ---
    public List<MonsterRule> Monsters { get; } = new();
    public List<string> FocusedMonsterNames { get; } = new();
    public string WeaponKey { get; set; } = "LeftShoulder"; // equip-weapon hotkey
    public string AmmoKey { get; set; } = "RightShoulder";  // equip-ammo hotkey
    public string AmmoBagKey { get; set; } = "";
    public bool EquipOnStart { get; set; } = false;
    public string AmmoRole { get; set; } = Roles.Ammo;     // LiveStats role holding ammo count
    public int StopAtAmmo { get; set; } = 0;               // halt attacking when ammo <= this (0 = ignore)
    public int ManualAmmoCount { get; set; }
    public int AmmoBags { get; set; }
    public int AmmoPerBag { get; set; } = 500;

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
    private readonly MouseSender _mouse;
    private readonly Random _rng = new();
    private readonly Dictionary<string, long> _skillCdUntil = new(StringComparer.OrdinalIgnoreCase);
    private int _skillIdx;
    private long _lastChangeTick, _lastMoveLogTick, _lastReconnectTick, _lastVisionDiagTick, _lastLoopDiagTick;
    private long _engagedSinceTick, _lastCombatProgressTick, _lastTargetSeenTick;
    private readonly Dictionary<string, long> _buffNextTick = new(StringComparer.OrdinalIgnoreCase);
    private int _lastExp = -1, _lastHp = -1, _lastPx = -1, _lastPy = -1, _lastWeight = -1, _lastAmmo = -1;
    private string _engagedTarget = "";
    private int _engagedTrackId;
    private int _castsOnTarget;
    private int _lowHpConfirmations;
    private bool _fleeLatched;
    private float _lastTargetHpRatio = -1;
    private float _avgHpDropPerCast = -1;
    private long _engagedMobMaxHp;
    private double _avgDamagePerCast = -1;
    private long _targetKillDeadlineTick;
    private int _expectedKillMs = -1;
    private bool _targetHadDamageProgress;
    private int _targetGoneScans;
    private int _targetLowHpScans;
    private SmartBotState _smartBotState = SmartBotState.Stopped;
    private static readonly Lazy<GameDatabase> _gameDb = new(() => new GameDatabase());

    private enum SmartBotState
    {
        Stopped,
        WaitingForClientFocus,
        WaitingForTrustedVitals,
        Buffing,
        SelectingTarget,
        EngagingTarget,
        ConfirmingKill,
        Roaming,
        RecoveringStuck
    }

    public SmartBotEngine(GameSession s, KeySender k, MouseSender m, HumanizedTiming t) : base("Smart Bot", s, k, t)
    {
        _mouse = m;
        _stat = new StatReader(s);
    }

    public override void ClearKeys()
    {
        AttackKey = LootKey = TeleportKey = ReturnKey = WeaponKey = AmmoKey = AmmoBagKey = "";
        SkillRotation.Clear(); BuffKeys.Clear(); ReconnectKeys.Clear();
        SkillSpRequired.Clear(); SkillDelayMsByKey.Clear(); ActionDelayMsByKey.Clear(); BuffIntervalByKeyMs.Clear(); _buffNextTick.Clear();
    }

    private void Log(BotLogKind kind, string text) { Report(text); BotLog.Instance.Add(kind, text); }

    private void Transition(SmartBotState next, string reason)
    {
        if (_smartBotState == next)
            return;
        var old = _smartBotState;
        _smartBotState = next;
        DebugTrace.Write("SmartBotState", $"old={old} new={next} reason={reason}");
    }

    private (int w, int h) ResolveClientSize()
    {
        var size = _mouse.ClientSize(Hwnd);
        if (size.w > 0 && size.h > 0)
            return size;

        try
        {
            var reattach = Session.Reattach();
            var retry = _mouse.ClientSize(Hwnd);
            DebugTrace.Write("SmartBot", $"ClientSize invalid hwnd=0x{Hwnd.ToInt64():X} first={size.w}x{size.h} reattachOk={reattach.Ok} retry={retry.w}x{retry.h}.");
            return retry;
        }
        catch (Exception ex)
        {
            DebugTrace.Write("SmartBot", $"ClientSize invalid hwnd=0x{Hwnd.ToInt64():X} first={size.w}x{size.h} reattachError='{ex.Message}'.");
            return size;
        }
    }

    private bool IsHpFresh(double hp)
    {
        if (hp <= 0 || hp > 100)
            return false;
        return LiveStats.Instance.TryGetTrustedNumber(Roles.HpPercent, out _);
    }

    private bool ShouldFleeForHp(double hp, bool hpFresh)
    {
        if (FleeAtHpPercent <= 0 || !hpFresh || hp <= 0 || hp > 100)
        {
            _lowHpConfirmations = 0;
            _fleeLatched = false;
            return false;
        }

        if (_fleeLatched)
        {
            if (hp >= FleeAtHpPercent + 8)
            {
                _fleeLatched = false;
                _lowHpConfirmations = 0;
                return false;
            }

            return true;
        }

        if (hp <= FleeAtHpPercent)
        {
            _lowHpConfirmations++;
            if (hp <= 1 || _lowHpConfirmations >= 2)
            {
                _fleeLatched = true;
                return true;
            }

            return false;
        }

        _lowHpConfirmations = 0;
        return false;
    }

    private void LogLoopDiagnostic(string branch, int cw, int ch, double hp, bool hpFresh, double wt)
    {
        long now = Environment.TickCount64;
        if (now - _lastLoopDiagTick < 500)
            return;
        _lastLoopDiagTick = now;

        var scene = LiveScene.Instance.Snapshot();
        int attackable = scene.Entities.Count(e => e.IsAttackable && IsMonster(e.Label));
        int confirmed = scene.Entities.Count(e => e.Confirmed);
        int visible = scene.Entities.Count(e => e.State == SceneTrackState.Visible);
        string sample = string.Join("; ", scene.Entities.Take(5).Select(e => $"#{e.TrackId}:{e.Label} atk={e.IsAttackable} hit={e.Hits} miss={e.Misses} state={e.State} box={e.X},{e.Y},{e.W}x{e.H}"));
        DebugTrace.Write("SmartBot",
            $"loop en={Enabled} attached={Session.Reader.Attached} statsFresh={LiveStats.Instance.IsFresh} " +
            $"hwnd=0x{Hwnd.ToInt64():X} cw={cw} ch={ch} " +
            $"sceneFresh={LiveScene.Instance.EntitiesFresh} sceneClientCoords={scene.ClientCoords} sceneAgeMs={SceneAgeMs()} statsAgeMs={StatsAgeMs()} " +
            $"entities={scene.Entities.Count} visible={visible} confirmed={confirmed} attackable={attackable} " +
            $"hp={hp:0} hpFresh={hpFresh} flee={FleeAtHpPercent} wt={wt:0} " +
            $"skills={SkillRotation.Count} buffs={BuffKeys.Count} branch={branch} sample=[{sample}].");
    }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        StartedAt = DateTime.Now; Kills = 0; _lastChangeTick = Environment.TickCount64;
        _lastCombatProgressTick = _lastChangeTick;
        _engagedTarget = "";
        if (EquipOnStart) EquipGear();

        while (!ct.IsCancellationRequested)
        {
            if (!Enabled)
            {
                Transition(SmartBotState.Stopped, "disabled");
                await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
                continue;
            }

            if (!Session.Reader.Attached && !LiveStats.Instance.IsFresh)
            {
                Transition(SmartBotState.WaitingForClientFocus, "not-attached-no-live-stats");
                await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
                continue;
            }

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
                        TapAction(key, 30);
                        await Timing.DelayAsync(ResolveActionDelayMs(key, AutoUtilityDelayMs(key, 1200), 250, 5000), ct);
                    }
                    await Timing.DelayAsync(ResolveActionDelayMs("Reconnect", AutoUtilityDelayMs("Reconnect", 3000), 1000, 8000), ct);
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

                if (BuffKeys.Count > 0)
                    Transition(SmartBotState.Buffing, "buff-refresh-check");
                await MaybeRefreshBuffs(ct);

                double hp = _stat.HpPercent;
                double wt = _stat.WeightPercent;
                bool hpFresh = IsHpFresh(hp);
                var (cw, ch) = ResolveClientSize();

                if (FleeAtHpPercent > 0 && !hpFresh)
                {
                    Transition(SmartBotState.WaitingForTrustedVitals, "trusted-hp-missing");
                    LogLoopDiagnostic("trusted-vitals", cw, ch, hp, hpFresh, wt);
                    await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
                    continue;
                }

                if (ReturnAtWeightPercent > 0 && wt >= 0 && wt >= ReturnAtWeightPercent)
                {
                    LogLoopDiagnostic("return", cw, ch, hp, hpFresh, wt);
                    Log(BotLogKind.Info, $"Weight {wt:0}% — returning to town.");
                    TapAction(ReturnKey, 20);
                    await Timing.DelayAsync(ResolveActionDelayMs(ReturnKey, AutoUtilityDelayMs(ReturnKey, 4000), 1000, 9000), ct);
                    continue;
                }

                if (FleeAtHpPercent > 0 && hpFresh && hp > 0 && hp <= FleeAtHpPercent && !ShouldFleeForHp(hp, hpFresh))
                {
                    LogLoopDiagnostic("flee-confirm", cw, ch, hp, hpFresh, wt);
                    await Timing.DelayAsync(ResolveActionDelayMs("FleeConfirm", AutoUtilityDelayMs("FleeConfirm", 120), 80, 500), ct);
                    continue;
                }

                if (ShouldFleeForHp(hp, hpFresh))
                {
                    Transition(SmartBotState.RecoveringStuck, "hp-flee-teleport");
                    LogLoopDiagnostic("flee", cw, ch, hp, hpFresh, wt);
                    Log(BotLogKind.Movement, $"HP {hp:0}% <= flee {FleeAtHpPercent}% - teleporting with {TeleportKey} before next target.");
                    TapAction(TeleportKey, 20);
                    ResetEngagedTarget();
                    await Timing.DelayAsync(ResolveActionDelayMs(TeleportKey, AutoUtilityDelayMs(TeleportKey, 400), 120, 1800), ct);
                    continue;
                }

                // ammo gate
                int ammo = ReadAmmo();
                bool ammoOk = !(StopAtAmmo > 0 && ammo >= 0 && ammo <= StopAtAmmo);
                if (!ammoOk)
                {
                    LogLoopDiagnostic("ammo", cw, ch, hp, hpFresh, wt);
                    if (TryUseAmmoBag(ammo))
                    {
                        await Timing.DelayAsync(ResolveActionDelayMs(AmmoBagKey, AutoUtilityDelayMs(AmmoBagKey, 900), 300, 6000), ct);
                        continue;
                    }
                    Log(BotLogKind.Ammo, $"Ammo {ammo} <= {StopAtAmmo} - holding fire.");
                    await Timing.DelayAsync(1500, ct);
                    continue;
                }

                bool visionActed = false;
                if (cw <= 0 || ch <= 0)
                {
                    Transition(SmartBotState.WaitingForClientFocus, "client-size-invalid");
                    LogLoopDiagnostic("client-size", cw, ch, hp, hpFresh, wt);
                    await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
                    continue;
                }

                if (UseVision && cw > 0 && ch > 0 &&
                    LiveScene.Instance.EntitiesFresh && LiveScene.Instance.ClientCoords)
                {
                    var pred = BuildTargetPredicate();
                    if (_engagedTrackId <= 0 && string.IsNullOrWhiteSpace(_engagedTarget))
                        Transition(SmartBotState.SelectingTarget, "vision-fresh-select");
                    var tgt = SelectTarget(cw / 2, ch / 2, pred);
                    if (tgt is { } t)
                    {
                        LogLoopDiagnostic("target", cw, ch, hp, hpFresh, wt);
                        LogVisionDecision("target", cw, ch, t);
                        MarkTargetSeen(t);
                        if (t.HasHp && t.HpRatio <= 0.04f && ConfirmLowHpScan(t))
                        {
                            Transition(SmartBotState.ConfirmingKill, "hp-empty-confirmed");
                            FinishEngagedTarget("HP empty confirmed");
                            visionActed = true;
                            await Timing.DelayAsync(ResolveNextMonsterDelayMs(t, cw, ch), ct);
                        }
                        else
                        {
                            Transition(SmartBotState.EngagingTarget, _castsOnTarget <= 0 ? "target-acquired" : "target-held");
                            int tx = Math.Clamp(t.Cx, 4, cw - 4), ty = Math.Clamp(t.Cy, 4, ch - 4);
                            // RO attack model: a skill is cast by pressing its hotkey FIRST, then clicking the
                            // target. With no skill assigned, a plain click is a normal (auto) attack.
                            string skillKey = "";
                            if (string.IsNullOrWhiteSpace(skillKey) && SkillRotation.Count > 0)
                            {
                                skillKey = SkillRotation[_skillIdx++ % SkillRotation.Count];
                            }
                            long now = Environment.TickCount64;
                            bool skillReady = !string.IsNullOrEmpty(skillKey)
                                && !(_skillCdUntil.TryGetValue(skillKey, out var until) && now < until);
                            bool hasEnoughSp = HasEnoughSpFor(skillKey);
                            var actionDelay = ResolveSkillDelayMs(skillKey, t, cw, ch);
                            if (!UpdateTargetKillDeadline(t, actionDelay))
                            {
                                visionActed = true;
                                await Timing.DelayAsync(ResolveActionDelayMs(TeleportKey, AutoUtilityDelayMs(TeleportKey, 500), 120, 1800), ct);
                                continue;
                            }

                            if (!string.IsNullOrEmpty(skillKey))
                            {
                                if (!skillReady)
                                {
                                    var wait = _skillCdUntil.TryGetValue(skillKey, out var untilTick)
                                        ? Math.Clamp((int)(untilTick - now), 25, Math.Min(250, Math.Max(40, actionDelay)))
                                        : Math.Clamp(actionDelay / 3, 40, 180);
                                    Log(BotLogKind.Skill, $"Waiting {wait} ms for {skillKey} cooldown on {TargetName(t)}.");
                                    visionActed = true;
                                    await Timing.DelayAsync(wait, ct);
                                }
                                else if (hasEnoughSp)
                                {
                                    _castsOnTarget++;
                                    TapAction(skillKey, 20);                       // arm the skill
                                    await Timing.DelayAsync(ResolveSkillArmDelayMs(skillKey, t, cw, ch), ct);
                                    ClickAt(tx, ty);                               // then left-click the monster
                                    _skillCdUntil[skillKey] = Environment.TickCount64 + Math.Max(80, actionDelay);
                                    Log(BotLogKind.Skill, $"Cast {_castsOnTarget}: {skillKey} on {TargetStatus(t)} @ {tx},{ty}");
                                    visionActed = true;
                                    MaybeCombatUnstuck();
                                    await Timing.DelayAsync(Math.Clamp(actionDelay, 80, 5000), ct);
                                }
                                else
                                {
                                    AttackTarget(tx, ty);
                                    Log(BotLogKind.Movement, $"SP low for {skillKey}; normal attack {TargetStatus(t)} @ {tx},{ty}");
                                    visionActed = true;
                                    MaybeCombatUnstuck();
                                    await Timing.DelayAsync(ResolveNormalAttackDelayMs(t, cw, ch), ct);
                                }
                            }
                            else
                            {
                                AttackTarget(tx, ty);                          // normal attack
                                Log(BotLogKind.Movement, $"Attack {TargetStatus(t)} ({t.Score:0.00}) @ {tx},{ty}");
                                visionActed = true;
                                MaybeCombatUnstuck();
                                await Timing.DelayAsync(ResolveNormalAttackDelayMs(t, cw, ch), ct);
                            }
                        }
                    }
                    else
                    {
                        LogLoopDiagnostic("no-target", cw, ch, hp, hpFresh, wt);
                        LogVisionDecision("no-target", cw, ch, null);
                        bool hadEngagedTarget = _engagedTrackId > 0;
                        if (hadEngagedTarget || !string.IsNullOrWhiteSpace(_engagedTarget))
                            Transition(SmartBotState.ConfirmingKill, "held-target-not-visible");
                        else
                            Transition(SmartBotState.SelectingTarget, "no-target-visible");
                        bool finished = TrackTargetGone();
                        if (finished)
                        {
                            visionActed = true;
                            await Timing.DelayAsync(ResolveNextMonsterDelayMs(null, cw, ch), ct);
                        }
                        else if (hadEngagedTarget && _engagedTrackId > 0)
                        {
                            visionActed = true;
                            await Timing.DelayAsync(80, ct);
                        }
                    }
                }
                else if (UseVision)
                {
                    Transition(SmartBotState.SelectingTarget, LiveScene.Instance.EntitiesFresh ? "scene-not-client-coords" : "scene-stale");
                    LogLoopDiagnostic(LiveScene.Instance.EntitiesFresh ? "scene-coords" : "scene-stale", cw, ch, hp, hpFresh, wt);
                }

                if (!visionActed && ClickToMove && cw > 0 && ch > 0)
                {
                    Transition(SmartBotState.Roaming, "no-target-roam");
                    LogLoopDiagnostic("roam", cw, ch, hp, hpFresh, wt);
                    // Nothing to fight in view: click a roam point to WALK there, then WAIT for the character
                    // to actually travel AND for the OCR to capture the new area before deciding again.
                    // (A click only issues a move order; walking + a screen read take time. Re-clicking before
                    // that finishes is why the character never seemed to move.)
                    var (x, y) = RoamPoint(cw, ch);
                    ClickAt(x, y);
                    if (Environment.TickCount64 - _lastMoveLogTick > 2000)
                    { _lastMoveLogTick = Environment.TickCount64; Log(BotLogKind.Movement, $"Walk -> {x},{y} (waiting to arrive)"); }
                    await WaitUntilArrivedAsync(cw, ch, x, y, ct);
                }

                TrackProgressAndUnstuck(ct);
                if (visionActed)
                    continue;
                LogLoopDiagnostic("idle", cw, ch, hp, hpFresh, wt);
            }
            await Timing.DelayAsync(ResolveIdleDelayMs(), ct);
        }
    }

    private void EquipGear()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(WeaponKey)) { TapAction(WeaponKey, 20); Log(BotLogKind.Info, $"Equip weapon ({WeaponKey})"); }
            if (!string.IsNullOrWhiteSpace(AmmoKey)) { TapAction(AmmoKey, 20); Log(BotLogKind.Info, $"Equip ammo ({AmmoKey})"); }
        }
        catch { }
    }

    /// <summary>Target every mob-looking label.</summary>
    private Func<SceneItem, bool> BuildTargetPredicate()
    {
        var focused = FocusedMonsterNames
            .Select(MonsterKey)
            .Where(k => k.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return item => item.IsAttackable
            && IsMonster(item.Label)
            && (focused.Count == 0 || IsGenericMonster(item.Label) || focused.Contains(MonsterKey(item.Label)));
    }

    private SceneItem? SelectTarget(int px, int py, Func<SceneItem, bool> match)
    {
        if (_engagedTrackId > 0)
        {
            foreach (var e in LiveScene.Instance.Entities)
                if (e.TrackId == _engagedTrackId && match(e))
                    return e;
        }

        SceneItem? bestNamed = null;
        SceneItem? bestGeneric = null;
        long bestNamedD = long.MaxValue;
        long bestGenericD = long.MaxValue;

        foreach (var e in LiveScene.Instance.Entities)
        {
            if (!match(e)) continue;
            long dx = e.Cx - px, dy = e.Cy - py, d = dx * dx + dy * dy;
            if (IsGenericMonster(e.Label))
            {
                if (d < bestGenericD) { bestGenericD = d; bestGeneric = e; }
            }
            else
            {
                if (d < bestNamedD)
                {
                    bestNamedD = d;
                    bestNamed = e;
                }
            }
        }

        return bestNamed ?? bestGeneric;
    }

    private void LogVisionDecision(string reason, int cw, int ch, SceneItem? chosen)
    {
        long now = Environment.TickCount64;
        if (reason != "target" && now - _lastVisionDiagTick < 1800)
            return;
        if (reason == "target" && now - _lastVisionDiagTick < 900)
            return;
        _lastVisionDiagTick = now;

        var entities = LiveScene.Instance.Entities;
        int visible = entities.Count(e => e.State == SceneTrackState.Visible);
        int confirmed = entities.Count(e => e.Confirmed);
        int attackable = entities.Count(e => e.IsAttackable && IsMonster(e.Label));
        string chosenText = chosen is { } c
            ? $"chosen=trk#{c.TrackId}:{c.Label} s={c.Score:0.00} hit={c.Hits} miss={c.Misses} state={c.State} conf={c.Confirmed} atk={c.IsAttackable} hp={(c.HasHp ? c.HpRatio.ToString("0.00") : "none")} box={c.X},{c.Y},{c.W}x{c.H} center={c.Cx},{c.Cy}"
            : "chosen=none";
        string sample = string.Join("; ", entities.Take(10).Select(e => $"trk#{e.TrackId}:{e.Label} s={e.Score:0.00} hit={e.Hits} miss={e.Misses} state={e.State} conf={e.Confirmed} atk={e.IsAttackable} box={e.X},{e.Y},{e.W}x{e.H}"));
        DebugTrace.Write("SmartBot",
            $"Vision decision reason={reason} useVision={UseVision} liveFresh={LiveScene.Instance.EntitiesFresh} clientCoords={LiveScene.Instance.ClientCoords} client={cw}x{ch} total={entities.Count} visible={visible} confirmed={confirmed} attackable={attackable} focused={FocusedMonsterNames.Count} {chosenText} sample=[{sample}].");
    }

    private void MarkTargetSeen(SceneItem item)
    {
        long now = Environment.TickCount64;
        string key = TargetKey(item);
        if (_engagedTrackId != item.TrackId || !string.Equals(_engagedTarget, key, StringComparison.OrdinalIgnoreCase))
        {
            _engagedTrackId = item.TrackId;
            _engagedTarget = key;
            _engagedSinceTick = now;
            _lastCombatProgressTick = now;
            _castsOnTarget = 0;
            _targetGoneScans = 0;
            _targetLowHpScans = 0;
            _lastTargetHpRatio = item.HasHp ? item.HpRatio : -1;
            _avgHpDropPerCast = -1;
            _engagedMobMaxHp = MonsterMaxHp(item.Label);
            _avgDamagePerCast = -1;
            _expectedKillMs = -1;
            _targetKillDeadlineTick = 0;
            _targetHadDamageProgress = false;
            DebugTrace.Write("SmartBot", $"Engaged target={key} mobHp={_engagedMobMaxHp} hpRatio={(item.HasHp ? item.HpRatio.ToString("0.00") : "none")}.");
        }
        else if (item.HasHp)
        {
            if (_lastTargetHpRatio >= 0 && item.HpRatio < _lastTargetHpRatio - 0.015f)
            {
                var drop = _lastTargetHpRatio - item.HpRatio;
                _avgHpDropPerCast = _avgHpDropPerCast < 0 ? drop : _avgHpDropPerCast * 0.65f + drop * 0.35f;
                if (_engagedMobMaxHp > 0)
                {
                    var damage = Math.Max(1.0, drop * _engagedMobMaxHp);
                    _avgDamagePerCast = _avgDamagePerCast < 0 ? damage : _avgDamagePerCast * 0.65 + damage * 0.35;
                }
                _targetHadDamageProgress = true;
                _lastCombatProgressTick = now;
                DebugTrace.Write("SmartBot", $"Damage learned target={_engagedTarget} hpDrop={drop:0.000} avgHpDrop={_avgHpDropPerCast:0.000} mobHp={_engagedMobMaxHp} avgDamage={_avgDamagePerCast:0}.");
            }
            _lastTargetHpRatio = item.HpRatio;
        }
        _lastTargetSeenTick = now;
        _targetGoneScans = 0;
    }

    private bool ConfirmLowHpScan(SceneItem item)
    {
        if (!item.HasHp || item.HpRatio > 0.04f)
        {
            _targetLowHpScans = 0;
            return false;
        }

        _targetLowHpScans++;
        return _targetLowHpScans >= 2;
    }

    private static string TargetKey(SceneItem item)
        => item.TrackId > 0 ? $"#{item.TrackId}|{item.Label}" : $"{item.Label}|{item.Cx / 24}|{item.Cy / 24}";

    private bool TrackTargetGone()
    {
        if (string.IsNullOrWhiteSpace(_engagedTarget) && _engagedTrackId <= 0) return false;
        long now = Environment.TickCount64;
        if (now - _lastTargetSeenTick < 700 || now - _engagedSinceTick < 500) return false;
        _targetGoneScans++;
        if (_targetGoneScans < 2)
        {
            Log(BotLogKind.Movement, $"Confirming target vanish scan {_targetGoneScans}/2 for {_engagedTarget.Split('|').LastOrDefault() ?? "monster"}.");
            return false;
        }
        if (_targetHadDamageProgress || _castsOnTarget >= 2 || (_expectedKillMs > 0 && now >= _targetKillDeadlineTick))
        {
            FinishEngagedTarget("target vanished after damage");
            return true;
        }
        else
        {
            Log(BotLogKind.Movement, $"Lost target {_engagedTarget.Split('|').LastOrDefault() ?? "monster"} before damage was confirmed.");
            ResetEngagedTarget();
            return false;
        }
    }

    private void FinishEngagedTarget(string reason)
    {
        if (string.IsNullOrWhiteSpace(_engagedTarget) && _engagedTrackId <= 0) return;
        Kills++;
        var elapsedMs = _engagedSinceTick > 0 ? Math.Max(0, Environment.TickCount64 - _engagedSinceTick) : 0;
        Log(BotLogKind.Kill, $"Kill #{Kills} ({_engagedTarget.Split('|').LastOrDefault() ?? "monster"}: {reason}, {elapsedMs / 1000.0:0.0}s, casts={_castsOnTarget}, expected={(_expectedKillMs > 0 ? $"{_expectedKillMs / 1000.0:0.0}s" : "auto-learning")})");
        TapAction(LootKey, 15);
        ResetEngagedTarget();
        _lastCombatProgressTick = Environment.TickCount64;
    }

    private void ResetEngagedTarget()
    {
        _engagedTarget = "";
        _engagedTrackId = 0;
        _castsOnTarget = 0;
        _targetGoneScans = 0;
        _targetLowHpScans = 0;
        _lastTargetHpRatio = -1;
        _avgHpDropPerCast = -1;
        _engagedMobMaxHp = 0;
        _avgDamagePerCast = -1;
        _targetKillDeadlineTick = 0;
        _expectedKillMs = -1;
        _targetHadDamageProgress = false;
    }

    private static string TargetName(SceneItem item)
        => item.TrackId > 0 ? $"#{item.TrackId} {item.Label}" : item.Label;

    private string TargetStatus(SceneItem item)
    {
        var name = TargetName(item);
        if (!item.HasHp)
            return name;
        var hp = Math.Clamp((int)Math.Round(item.HpRatio * 100), 1, 100);
        if (_avgHpDropPerCast > 0.005f)
        {
            var casts = Math.Clamp((int)Math.Ceiling(item.HpRatio / _avgHpDropPerCast), 1, 99);
            var seconds = EstimateSecondsLeft(item, ResolveSkillDelayMs("", item, 1280, 720));
            return $"{name} HP {hp}% (~{casts} cast{(casts == 1 ? "" : "s")} / {seconds:0.0}s left)";
        }
        var eta = EstimateSecondsLeft(item, ResolveSkillDelayMs("", item, 1280, 720));
        return eta > 0 ? $"{name} HP {hp}% (~{eta:0.0}s left)" : $"{name} HP {hp}%";
    }

    private bool UpdateTargetKillDeadline(SceneItem item, int actionDelayMs)
    {
        int expected = ResolveFocusKillMs(item, actionDelayMs);
        if (expected <= 0)
            return true;
        _expectedKillMs = expected;
        long now = Environment.TickCount64;
        if (_targetKillDeadlineTick <= 0)
            _targetKillDeadlineTick = _engagedSinceTick + expected;
        if (now > _targetKillDeadlineTick && now - _lastCombatProgressTick > Math.Max(900, actionDelayMs * 2L))
        {
            Log(BotLogKind.Movement, $"Target exceeded expected kill time ({expected / 1000.0:0.0}s) without HP/EXP progress - teleporting.");
            TapAction(TeleportKey, 20);
            ResetEngagedTarget();
            _lastCombatProgressTick = Environment.TickCount64;
            _lastChangeTick = _lastCombatProgressTick;
            return false;
        }
        return true;
    }

    private int ResolveFocusKillMs(SceneItem item, int actionDelayMs)
    {
        if (FocusKillMs >= 0)
            return Math.Clamp(FocusKillMs, 1000, 600_000);

        int delay = Math.Clamp(actionDelayMs <= 0 ? ResolveNormalAttackDelayMs(item, 1280, 720) : actionDelayMs, 80, 5000);
        double hpRatio = item.HasHp ? Math.Clamp(item.HpRatio, 0.01f, 1.0f) : 1.0;

        if (_avgHpDropPerCast > 0.005f)
        {
            int casts = Math.Clamp((int)Math.Ceiling(hpRatio / _avgHpDropPerCast), 1, 120);
            return Math.Clamp(casts * delay + 900, 900, 120_000);
        }

        if (_engagedMobMaxHp <= 0)
            _engagedMobMaxHp = MonsterMaxHp(item.Label);
        if (_engagedMobMaxHp > 0 && _avgDamagePerCast > 1)
        {
            double hpLeft = _engagedMobMaxHp * hpRatio;
            int casts = Math.Clamp((int)Math.Ceiling(hpLeft / _avgDamagePerCast), 1, 120);
            return Math.Clamp(casts * delay + 900, 900, 120_000);
        }

        return AutoFocusKillMsFromMonsterHp(item, delay);
    }

    private double EstimateSecondsLeft(SceneItem item, int actionDelayMs)
        => ResolveFocusKillMs(item, actionDelayMs) / 1000.0;

    private int AutoFocusKillMsFromMonsterHp(SceneItem item, int delay)
    {
        long hp = _engagedMobMaxHp > 0 ? _engagedMobMaxHp : MonsterMaxHp(item.Label);
        double hpRatio = item.HasHp ? Math.Clamp(item.HpRatio, 0.05f, 1.0f) : 1.0;
        if (hp <= 0)
            return Math.Clamp(4 * delay + 1600, 1800, 14_000);

        double estimatedCasts = Math.Sqrt(Math.Max(1, hp)) / 6.0 * hpRatio;
        int casts = Math.Clamp((int)Math.Ceiling(estimatedCasts), 2, 90);
        return Math.Clamp(casts * delay + 1400, 1800, 90_000);
    }

    private static long MonsterMaxHp(string label)
    {
        try
        {
            var mob = _gameDb.Value.MobByName(label) ?? _gameDb.Value.MobByTrainingLabel(label);
            return mob?.Hp > 0 ? mob.Hp : 0;
        }
        catch { return 0; }
    }

    private bool HasEnoughSpFor(string skillKey)
    {
        if (string.IsNullOrWhiteSpace(skillKey)) return true;
        if (!SkillSpRequired.TryGetValue(skillKey, out var need) || need <= 0) return true;
        int sp = _stat.Sp;
        return sp < 0 || sp >= need;
    }

    private async Task MaybeRefreshBuffs(CancellationToken ct)
    {
        if (BuffKeys.Count == 0) return;
        long now = Environment.TickCount64;

        foreach (var key in BuffKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            var interval = BuffIntervalByKeyMs.TryGetValue(key, out var perKeyMs)
                ? Math.Max(5000, perKeyMs)
                : Math.Max(5000, BuffIntervalMs);
            if (_buffNextTick.TryGetValue(key, out var next) && now < next) continue;
            _buffNextTick[key] = now + interval;
            TapAction(key, 20);
            Log(BotLogKind.Skill, $"Buff {key} (next in {interval / 1000}s)");
            await Timing.DelayAsync(450, ct);
        }
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
    {
        if (LiveStats.Instance.TryGetNumber(AmmoRole, out var v)) return v;
        return ManualAmmoCount > 0 ? ManualAmmoCount : -1;
    }

    private bool TryUseAmmoBag(int currentAmmo)
    {
        if (string.IsNullOrWhiteSpace(AmmoBagKey) || AmmoBags <= 0)
            return false;
        TapAction(AmmoBagKey, 25);
        AmmoBags = Math.Max(0, AmmoBags - 1);
        ManualAmmoCount = Math.Max(ManualAmmoCount, Math.Max(currentAmmo, 0)) + Math.Max(1, AmmoPerBag);
        Log(BotLogKind.Ammo, $"Ammo low ({currentAmmo}); used ammo bag {AmmoBagKey}. Bags left: {AmmoBags}. Manual ammo now ~{ManualAmmoCount}.");
        return true;
    }

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
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        return !label.Equals("loot", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("portal", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("player", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("target", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("target hp", StringComparison.OrdinalIgnoreCase)
            && !label.Equals("player hp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenericMonster(string label)
        => label.Equals("Monster", StringComparison.OrdinalIgnoreCase)
        || label.Equals("Mob", StringComparison.OrdinalIgnoreCase)
        || label.Equals("Entity", StringComparison.OrdinalIgnoreCase);

    private static string MonsterKey(string? value)
        => GameDatabase.NormalizeKey(value);

    private void ClickAt(int x, int y)
    {
        DebugTrace.Write("SmartBot", $"ClickAt requested hwnd=0x{Hwnd.ToInt64():X} client={x},{y} hardware={HardwareClick} mouseMethod={_mouse.Method} virtualButton={_mouse.VirtualLeftClickButton} hold={_mouse.VirtualClickHoldMs}.");
        if (HardwareClick)
            _mouse.HardwareClick(Hwnd, x, y);
        else
            _mouse.Click(Hwnd, x, y);
    }

    private void AttackTarget(int x, int y)
    {
        ClickAt(x, y);
    }

    private void TapAction(string action, int holdMs)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        int vk = KeyName.ToVk(action);

        // RO hotbar skills are keyboard hotkeys. When VIIPER/FakerInput is selected, send the
        // actual key first; a ViGEm controller button only works when an external profile maps it
        // back to the RO key, so it must be a fallback rather than the primary skill path.
        if (Keys.Method != InputMethod.ReWasdClick && vk > 0)
        {
            DebugTrace.Write("SmartBot", $"TapAction keyboard-first action='{action}' vk={vk} method={Keys.Method} holdMs={holdMs}.");
            Keys.Tap(Hwnd, vk, Math.Max(60, holdMs));
            return;
        }

        if (UseControllerButtons)
        {
            var button = ResolveControllerButton(action);
            DebugTrace.Write("SmartBot", $"TapAction controller action='{action}' resolved='{button}' holdMs={holdMs} mapCount={ControllerKeyMap.Count}.");
            if (!string.IsNullOrWhiteSpace(button))
            {
                if (_mouse.TapVirtualButton(button, holdMs))
                    return;
                DebugTrace.Write("SmartBot", $"TapAction controller failed for '{action}', trying keyboard fallback.");
            }
            else
                DebugTrace.Write("SmartBot", $"TapAction dropped: no controller mapping for '{action}'.");

            if (!_mouse.IsReWasdRunning() && KeyName.ToVk(action) > 0)
            {
                DebugTrace.Write("SmartBot", $"Controller bridge is not running; sending real key fallback for '{action}'.");
                Keys.TapSendInputFallback(Hwnd, KeyName.ToVk(action), holdMs);
            }
        }
        else
        {
            DebugTrace.Write("SmartBot", $"TapAction keyboard action='{action}' vk={vk} holdMs={holdMs}.");
            Keys.Tap(Hwnd, vk, holdMs);
        }
    }

    private string ResolveControllerButton(string action)
    {
        return ControllerKeyMap.TryGetValue(action, out var mapped) && ReWasdMouseMap.IsButtonChord(mapped)
            ? ReWasdMouseMap.NormalizeChord(mapped)
            : ReWasdMouseMap.NormalizeChord(action);
    }

    private void TrackProgressAndUnstuck(CancellationToken ct)
    {
        int exp = _stat.Exp, hp = _stat.Hp, px = _stat.PosX, py = _stat.PosY;
        int weight = _stat.Weight, ammo = ReadAmmo();
        bool changed =
            (exp >= 0 && exp != _lastExp) ||
            (hp >= 0 && hp != _lastHp) ||
            (px >= 0 && (px != _lastPx || py != _lastPy));
        if (exp > _lastExp && _lastExp >= 0)
        {
            Kills++;
            _lastCombatProgressTick = Environment.TickCount64;
            ResetEngagedTarget();
            Log(BotLogKind.Kill, $"Kill #{Kills} (EXP {_lastExp} -> {exp})");
        }
        if (weight >= 0 && _lastWeight >= 0 && weight > _lastWeight) Log(BotLogKind.Item, $"Item picked (weight {_lastWeight} -> {weight})");
        if (ammo >= 0 && _lastAmmo >= 0 && ammo != _lastAmmo) Log(BotLogKind.Ammo, $"Ammo left: {ammo}");
        if (changed) _lastChangeTick = Environment.TickCount64;
        _lastExp = exp; _lastHp = hp; _lastPx = px; _lastPy = py;
        if (weight >= 0) _lastWeight = weight;
        if (ammo >= 0) _lastAmmo = ammo;
        MaybeUnstuck(ct);
    }

    private void MaybeCombatUnstuck()
    {
        if (string.IsNullOrWhiteSpace(_engagedTarget)) return;
        if (Environment.TickCount64 - _lastCombatProgressTick < Math.Max(2000, StuckMs)) return;
        var focus = _expectedKillMs > 0 ? $"{_expectedKillMs / 1000.0:0.0}s expected" : "auto expected";
        Log(BotLogKind.Movement, $"Could not finish {_engagedTarget.Split('|').LastOrDefault() ?? "monster"} for {StuckMs}ms ({focus}) - teleporting.");
        TapAction(TeleportKey, 20);
        ResetEngagedTarget();
        _lastCombatProgressTick = Environment.TickCount64;
        _lastChangeTick = _lastCombatProgressTick;
    }

    private int ResolveIdleDelayMs()
    {
        if (RotationMs >= 0)
            return Math.Clamp(RotationMs, 10, 5000);

        int scene = SceneAgeMs() < 2500 ? Math.Clamp(SceneAgeMs() / 10, 0, 120) : 180;
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 20, 0, 90) : 120;
        return Math.Clamp(55 + InputBackendPenaltyMs() + scene + stats, 45, 380);
    }

    private int ResolveSkillDelayMs(string skillKey, SceneItem target, int clientW, int clientH)
    {
        int manual = -1;
        if (!string.IsNullOrWhiteSpace(skillKey) && SkillDelayMsByKey.TryGetValue(skillKey, out var configured))
            manual = configured;
        else if (RotationMs >= 0)
            manual = RotationMs;
        if (manual >= 0)
            return Math.Clamp(manual, 10, 5000);

        var metrics = TimingMetrics(target, clientW, clientH);
        int castBase = HasEnoughSpFor(skillKey) ? 120 : 80;
        int mouseTravel = (int)Math.Round(45 + 230 * metrics.DistanceRatio);
        int aim = (int)Math.Round(90 * metrics.SmallTargetPenalty + 55 * metrics.LowConfidencePenalty);
        int tracker = target.TrackId > 0 ? Math.Clamp(target.Misses * 28 - target.Hits * 3, -35, 90) : 80;
        int hp = target.HasHp ? 20 : 75;
        int freshness = (int)Math.Round(metrics.SceneAgePenalty + metrics.StatsAgePenalty * 0.45);
        int engagement = _engagedTrackId == target.TrackId && target.TrackId > 0 ? -35 : 35;
        int screen = (int)Math.Round(35 * metrics.CaptureScalePenalty);
        int input = InputBackendPenaltyMs() + (UseControllerButtons ? 20 : 0) + (HardwareClick ? 18 : 35);
        int sp = HasEnoughSpFor(skillKey) ? 0 : -45;

        int auto = Math.Clamp(castBase + mouseTravel + aim + tracker + hp + freshness + engagement + screen + input + sp, 110, 1500);
        var learned = SmartBotTrainingTuning.Instance.Current;
        return SmartBotTrainingTuning.BlendAuto(auto, learned.SkillDelayMs, TrainingWeight(learned), 110, 1500);
    }

    private int ResolveSkillArmDelayMs(string skillKey, SceneItem target, int clientW, int clientH)
    {
        var metrics = TimingMetrics(target, clientW, clientH);
        int key = string.IsNullOrWhiteSpace(skillKey) ? 0 : Math.Clamp(skillKey.Length * 2, 4, 24);
        int travel = (int)Math.Round(18 + 70 * metrics.DistanceRatio);
        int confidence = (int)Math.Round(35 * metrics.LowConfidencePenalty);
        int backend = Math.Clamp(InputBackendPenaltyMs() / 3, 8, 36);
        return Math.Clamp(key + travel + confidence + backend, 35, 180);
    }

    private int ResolveNormalAttackDelayMs(SceneItem target, int clientW, int clientH)
    {
        var metrics = TimingMetrics(target, clientW, clientH);
        int mouseTravel = (int)Math.Round(35 + 165 * metrics.DistanceRatio);
        int aim = (int)Math.Round(60 * metrics.SmallTargetPenalty + 40 * metrics.LowConfidencePenalty);
        int freshness = (int)Math.Round(metrics.SceneAgePenalty * 0.65);
        int tracker = target.TrackId > 0 ? Math.Clamp(target.Misses * 20 - target.Hits * 2, -25, 70) : 60;
        int auto = Math.Clamp(70 + mouseTravel + aim + freshness + tracker + InputBackendPenaltyMs(), 90, 700);
        var learned = SmartBotTrainingTuning.Instance.Current;
        return SmartBotTrainingTuning.BlendAuto(auto, learned.NormalAttackDelayMs, TrainingWeight(learned), 90, 700);
    }

    private int ResolveActionDelayMs(string actionKey, int autoMs, int minMs, int maxMs)
    {
        int configured = -1;
        if (!string.IsNullOrWhiteSpace(actionKey) && ActionDelayMsByKey.TryGetValue(actionKey, out var rowDelay))
            configured = rowDelay;
        if (configured >= 0)
            return Math.Clamp(configured, minMs, maxMs);
        return Math.Clamp(autoMs, minMs, maxMs);
    }

    private int AutoUtilityDelayMs(string actionKey, int baseMs)
    {
        var (cw, ch) = _mouse.ClientSize(Hwnd);
        double pixelScale = Math.Sqrt(Math.Max(1, cw) * Math.Max(1, ch)) / Math.Sqrt(1280.0 * 720.0);
        int capture = (int)Math.Round(40 * Math.Clamp(pixelScale, 0.65, 2.2));
        int scene = SceneAgeMs() < 2500 ? Math.Clamp(SceneAgeMs() / 12, 0, 150) : 180;
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 18, 0, 120) : 150;
        int input = InputBackendPenaltyMs() + (UseControllerButtons ? 25 : 0);
        int keyComplexity = string.IsNullOrWhiteSpace(actionKey) ? 20 : Math.Clamp(actionKey.Length * 4, 12, 60);
        int auto = Math.Clamp(baseMs + capture + scene + stats + input + keyComplexity, 80, 9000);
        var learned = SmartBotTrainingTuning.Instance.Current;
        var key = (actionKey ?? "").Trim();
        int? observed = key.Equals(TeleportKey, StringComparison.OrdinalIgnoreCase) ? learned.TeleportDelayMs : null;
        return SmartBotTrainingTuning.BlendAuto(auto, observed, TrainingWeight(learned), 80, 9000);
    }

    private int AutoMoveWaitMs(int clientW, int clientH, int targetX, int targetY)
    {
        if (MoveWaitMs >= 0)
            return Math.Clamp(MoveWaitMs, 400, 5000);

        double diagonal = Math.Max(1.0, Math.Sqrt(clientW * clientW + clientH * clientH));
        double dx = targetX - clientW / 2.0;
        double dy = targetY - clientH / 2.0;
        double distanceRatio = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / diagonal, 0.0, 1.0);
        double captureScale = Math.Sqrt(Math.Max(1, clientW) * Math.Max(1, clientH)) / Math.Sqrt(1280.0 * 720.0);
        int scene = SceneAgeMs() < 2500 ? Math.Clamp(SceneAgeMs() / 8, 0, 220) : 260;
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 10, 0, 250) : 300;
        int input = InputBackendPenaltyMs() + (HardwareClick ? 25 : 45);
        int travel = (int)Math.Round(520 + 1450 * distanceRatio * Math.Clamp(captureScale, 0.70, 1.80));
        int auto = Math.Clamp(travel + scene + stats + input, 550, 3200);
        var learned = SmartBotTrainingTuning.Instance.Current;
        return SmartBotTrainingTuning.BlendAuto(auto, learned.WalkWaitMs, TrainingWeight(learned), 550, 3200);
    }

    private int ResolveNextMonsterDelayMs(SceneItem? lastTarget, int clientW, int clientH)
    {
        if (NextMonsterDelayMs >= 0)
            return Math.Clamp(NextMonsterDelayMs, 0, 5000);

        int scene = SceneAgeMs() < 2500 ? Math.Clamp(SceneAgeMs() / 6, 0, 220) : 260;
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 8, 0, 220) : 260;
        int input = InputBackendPenaltyMs() + (HardwareClick ? 20 : 40);
        int distance = 0;
        if (lastTarget is { } target && clientW > 0 && clientH > 0)
        {
            double diagonal = Math.Max(1.0, Math.Sqrt(clientW * clientW + clientH * clientH));
            double dx = target.Cx - clientW / 2.0;
            double dy = target.Cy - clientH / 2.0;
            distance = (int)Math.Clamp((Math.Sqrt(dx * dx + dy * dy) / diagonal) * 180.0, 0, 180);
        }

        int auto = Math.Clamp(140 + scene + stats + input + distance, 120, 1100);
        var learned = SmartBotTrainingTuning.Instance.Current;
        int observed = learned.WalkWaitMs is > 0 ? Math.Clamp(learned.WalkWaitMs.Value / 3, 100, 1200) : 0;
        return SmartBotTrainingTuning.BlendAuto(auto, observed, TrainingWeight(learned), 100, 1200);
    }

    private int AutoMoveStableMs(int moveWaitMs)
    {
        if (MoveStableMs >= 0)
            return Math.Clamp(MoveStableMs, 150, 3000);

        int scene = SceneAgeMs() < 2500 ? Math.Clamp(SceneAgeMs() / 18, 0, 90) : 120;
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 18, 0, 90) : 120;
        return Math.Clamp(moveWaitMs / 4 + scene + stats, 220, 750);
    }

    private (double DistanceRatio, double SmallTargetPenalty, double LowConfidencePenalty, double SceneAgePenalty, double StatsAgePenalty, double CaptureScalePenalty) TimingMetrics(SceneItem target, int clientW, int clientH)
    {
        double diagonal = Math.Max(1.0, Math.Sqrt(clientW * clientW + clientH * clientH));
        double dx = target.Cx - clientW / 2.0;
        double dy = target.Cy - clientH / 2.0;
        double distanceRatio = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / diagonal, 0.0, 1.0);
        double targetArea = Math.Max(1.0, target.W * target.H);
        double screenArea = Math.Max(1.0, clientW * clientH);
        double targetShare = Math.Clamp(targetArea / screenArea, 0.00001, 0.08);
        double smallTargetPenalty = Math.Clamp((0.006 - targetShare) / 0.006, 0.0, 1.0);
        double lowConfidencePenalty = Math.Clamp((0.95 - target.Score) / 0.55, 0.0, 1.0);
        double sceneAgePenalty = SceneAgeMs() < 2500 ? Math.Clamp(SceneAgeMs() * 0.12, 0, 180) : 220;
        double statsAgePenalty = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() * 0.05, 0, 120) : 150;
        double captureScalePenalty = Math.Clamp(Math.Sqrt(screenArea) / Math.Sqrt(1280.0 * 720.0) - 1.0, 0.0, 1.5);
        return (distanceRatio, smallTargetPenalty, lowConfidencePenalty, sceneAgePenalty, statsAgePenalty, captureScalePenalty);
    }

    private int SceneAgeMs()
        => LiveScene.Instance.EntityUpdatedUtc == DateTime.MinValue
            ? 5000
            : (int)Math.Clamp((DateTime.UtcNow - LiveScene.Instance.EntityUpdatedUtc).TotalMilliseconds, 0, 5000);

    private int StatsAgeMs()
        => LiveStats.Instance.UpdatedUtc == DateTime.MinValue
            ? 5000
            : (int)Math.Clamp((DateTime.UtcNow - LiveStats.Instance.UpdatedUtc).TotalMilliseconds, 0, 5000);

    private int InputBackendPenaltyMs()
        => Keys.Method switch
        {
            InputMethod.Viiper => 22,
            InputMethod.VirtualHid => 38,
            InputMethod.SendInput => 65,
            InputMethod.MouseKeyEvent => 70,
            InputMethod.ReWasdClick => 55,
            InputMethod.PostMessage => 95,
            _ => 75
        };

    private static double TrainingWeight(SmartBotTrainingTuning.Snapshot learned)
        => learned.SampleCount <= 0 ? 0.0 : Math.Clamp(learned.SampleCount / 50.0, 0.10, 0.45);

    /// <summary>After a walk order, watch the OCR position (PosX/PosY) and return once it stops changing for a
    /// short spell (arrived) — or after MoveWaitMs at most. This is how the bot reads moving vs stopped from
    /// the numbers, now that the Character motion box is hidden.</summary>
    private async Task WaitUntilArrivedAsync(int clientW, int clientH, int targetX, int targetY, CancellationToken ct)
    {
        int moveWaitMs = AutoMoveWaitMs(clientW, clientH, targetX, targetY);
        int stableMs = AutoMoveStableMs(moveWaitMs);
        long deadline = Environment.TickCount64 + moveWaitMs;
        int lastX = _stat.PosX, lastY = _stat.PosY;
        long stableSince = Environment.TickCount64;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            await Timing.DelayAsync(120, ct);
            int x = _stat.PosX, y = _stat.PosY;
            if (x < 0 || y < 0) continue;                       // no position read -> use the time cap
            if (x != lastX || y != lastY)
            { lastX = x; lastY = y; stableSince = Environment.TickCount64; }   // still moving
            else if (Environment.TickCount64 - stableSince >= stableMs)
                return;                                          // held 400ms -> arrived / stopped
        }
    }

    private void MaybeUnstuck(CancellationToken ct)
    {
        if (Environment.TickCount64 - _lastChangeTick < Math.Max(2000, StuckMs)) return;
        Log(BotLogKind.Movement, $"No progress for {StuckMs}ms — teleporting.");
        TapAction(TeleportKey, 20);
        _lastChangeTick = Environment.TickCount64;
    }
}
