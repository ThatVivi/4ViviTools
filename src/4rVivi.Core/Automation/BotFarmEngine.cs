using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Simple attack/loot rotation with a flee-at-HP% safety. Private-server use.</summary>
public sealed class BotFarmEngine : AutomationEngine
{
    public string AttackKey { get; set; } = "F1";
    public string LootKey { get; set; } = "Z";
    public int FleeAtHpPercent { get; set; } = 25;
    public int RotationMs { get; set; } = 350;

    public int Kills { get; private set; }
    public DateTime StartedAt { get; private set; } = DateTime.Now;

    public BotFarmEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Bot / Farm", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        StartedAt = DateTime.Now; Kills = 0;
        while (!ct.IsCancellationRequested)
        {
            if (Session.MasterEnabled && Session.Reader.Attached)
            {
                double hp = Session.Health.HpPercent;
                if (hp >= 0 && hp <= FleeAtHpPercent)
                {
                    Report($"HP {hp:0}% — holding (flee threshold).");
                    await Timing.DelayAsync(500, ct);
                    continue;
                }
                Keys.Tap(Hwnd, KeyName.ToVk(AttackKey), 15);
                await Timing.DelayAsync(RotationMs / 2, ct);
                Keys.Tap(Hwnd, KeyName.ToVk(LootKey), 15);
                Kills++;
            }
            await Timing.DelayAsync(RotationMs, ct);
        }
    }
}
