using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>ro-tools "Auto Ygg" — emergency Yggdrasil item when HP% (and optionally SP%) drop below a
/// threshold. OCR-driven via Session.Health (HP/SP %). Faithful to events/auto_ygg.py.</summary>
public sealed class AutoYggEngine : AutomationEngine
{
    public string Key { get; set; } = "F10";
    public override void ClearKeys() { Key = ""; }
    public int HpPercent { get; set; } = 25;
    public int SpPercent { get; set; } = 0;     // 0 = ignore SP (HP-only emergency)
    public int CooldownMs { get; set; } = 1500;
    private long _lastFire;
    private int _lowHpConfirmations;

    public AutoYggEngine(GameSession s, KeySender k, HumanizedTiming t) : base("AutoYgg", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Enabled && (Session.Reader.Attached || LiveStats.Instance.IsFresh))
            {
                double hp = Session.Health.HpPercent, sp = Session.Health.SpPercent;
                long now = Environment.TickCount64;
                bool hpTrip = hp >= 0 && hp <= HpPercent;
                bool spOk = SpPercent <= 0 || (sp >= 0 && sp <= SpPercent);  // SP gate optional
                if (!hpTrip) _lowHpConfirmations = 0;
                if (hpTrip && hp > 1 && ++_lowHpConfirmations < 2)
                {
                    await Timing.DelayAsync(60, ct);
                    continue;
                }
                if (hpTrip && spOk && now - _lastFire >= CooldownMs && !string.IsNullOrWhiteSpace(Key))
                {
                    Keys.Tap(Hwnd, KeyName.ToVk(Key), 15);
                    _lastFire = now;
                    Report($"Ygg @ HP {hp:0}%");
                }
            }
            await Timing.DelayAsync(60, ct);
        }
    }
}
