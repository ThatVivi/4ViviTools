using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Input-level auto-combat: attack + skill rotation, loot, anti-stuck teleport,
/// return-to-town when overweight. Uses bound role addresses; degrades when they are unknown.</summary>
public sealed class SmartBotEngine : AutomationEngine
{
    public string AttackKey { get; set; } = "F1";
    public List<string> SkillRotation { get; } = new();   // optional skills to weave in
    public string LootKey { get; set; } = "Z";
    public string TeleportKey { get; set; } = "F12";       // fly wing / teleport hotkey
    public string ReturnKey { get; set; } = "F11";         // butterfly wing / town macro
    public int FleeAtHpPercent { get; set; } = 25;
    public int StuckSeconds { get; set; } = 8;             // no EXP/HP change -> teleport
    public int ReturnAtWeightPercent { get; set; } = 90;
    public int RotationMs { get; set; } = 350;
    public bool ClickToMove { get; set; } = true;   // walk by clicking random nearby points
    public int MoveRadius { get; set; } = 180;       // px around screen centre

    public int Kills { get; private set; }                  // proxied from EXP gains
    public DateTime StartedAt { get; private set; } = DateTime.Now;

    private readonly StatReader _stat;
    private readonly MouseSender _mouse = new();
    private readonly Random _rng = new();
    private int _skillIdx;
    private long _lastChangeTick;
    private int _lastExp = -1, _lastHp = -1, _lastPx = -1, _lastPy = -1;

    public SmartBotEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Smart Bot", s, k, t)
        => _stat = new StatReader(s);

    protected override async Task LoopAsync(CancellationToken ct)
    {
        StartedAt = DateTime.Now; Kills = 0; _lastChangeTick = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            if (Session.MasterEnabled && Session.Reader.Attached)
            {
                double hp = _stat.HpPercent;
                double wt = _stat.WeightPercent;

                // return to town when overweight
                if (wt >= 0 && wt >= ReturnAtWeightPercent)
                {
                    Report($"Weight {wt:0}% — returning to town.");
                    Keys.Tap(Hwnd, KeyName.ToVk(ReturnKey), 20);
                    await Timing.DelayAsync(4000, ct);
                    continue;
                }

                // safety: very low HP -> hold (autopot heals); don't feed mobs
                if (hp >= 0 && hp <= FleeAtHpPercent)
                {
                    await Timing.DelayAsync(400, ct);
                    MaybeUnstuck(ct);
                    continue;
                }

                // walk: click a random nearby point so the character moves and finds mobs
                if (ClickToMove)
                {
                    var (cw, ch) = _mouse.ClientSize(Hwnd);
                    if (cw > 0 && ch > 0)
                    {
                        int cx = cw / 2, cy = ch / 2;
                        int x = Math.Clamp(cx + _rng.Next(-MoveRadius, MoveRadius), 4, cw - 4);
                        int y = Math.Clamp(cy + _rng.Next(-MoveRadius, MoveRadius), 4, ch - 4);
                        _mouse.Click(Hwnd, x, y);
                        await Timing.DelayAsync(RotationMs / 2, ct);
                    }
                }

                // attack + weave the user-registered skills
                Keys.Tap(Hwnd, KeyName.ToVk(AttackKey), 15);
                if (SkillRotation.Count > 0)
                {
                    await Timing.DelayAsync(RotationMs / 3, ct);
                    Keys.Tap(Hwnd, KeyName.ToVk(SkillRotation[_skillIdx++ % SkillRotation.Count]), 15);
                }
                await Timing.DelayAsync(RotationMs / 3, ct);
                Keys.Tap(Hwnd, KeyName.ToVk(LootKey), 15);

                TrackProgressAndUnstuck(ct);
            }
            await Timing.DelayAsync(RotationMs, ct);
        }
    }

    private void TrackProgressAndUnstuck(CancellationToken ct)
    {
        int exp = _stat.Exp, hp = _stat.Hp, px = _stat.PosX, py = _stat.PosY;
        bool changed =
            (exp >= 0 && exp != _lastExp) ||
            (hp >= 0 && hp != _lastHp) ||
            (px >= 0 && (px != _lastPx || py != _lastPy));
        if (exp > _lastExp && _lastExp >= 0) Kills++;
        if (changed) _lastChangeTick = Environment.TickCount64;
        _lastExp = exp; _lastHp = hp; _lastPx = px; _lastPy = py;
        MaybeUnstuck(ct);
    }

    private void MaybeUnstuck(CancellationToken ct)
    {
        if (Environment.TickCount64 - _lastChangeTick < StuckSeconds * 1000L) return;
        Report($"No progress for {StuckSeconds}s — teleporting.");
        Keys.Tap(Hwnd, KeyName.ToVk(TeleportKey), 20);
        _lastChangeTick = Environment.TickCount64;
    }
}
