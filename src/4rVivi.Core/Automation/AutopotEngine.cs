using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Settings;

namespace FourRVivi.Core.Automation;

/// <summary>sickpot-grade autopot: per-rule HP/SP %+flat thresholds, reaction time, per-key use-delay.</summary>
public sealed class AutopotEngine : AutomationEngine
{
    public List<PotConfig> Rules { get; } = new();
    public bool Mouseboost { get; set; } = true;   // write to a bound 'Mouseboost' address to bypass item delay
    private readonly Dictionary<PotConfig, long> _lastFire = new();

    public AutopotEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Autopot", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Session.MasterEnabled && Session.Reader.Attached)
            {
                double hp = Session.Health.HpPercent, sp = Session.Health.SpPercent;
                int hpv = Session.Health.Hp, spv = Session.Health.Sp;
                long now = Environment.TickCount64;

                foreach (var r in Rules)
                {
                    if (!r.Enabled) continue;
                    double pct = r.UseSp ? sp : hp;
                    int flat = r.UseSp ? spv : hpv;
                    if (pct < 0) continue; // address unknown
                    bool trip = pct <= r.Percent || (r.Flat > 0 && flat <= r.Flat);
                    if (!trip) continue;
                    if (_lastFire.TryGetValue(r, out long last) && now - last < r.UseDelayMs) continue;

                    await Timing.DelayAsync(r.ReactionMs, ct);
                    if (Mouseboost) TryMouseboost();
                    Keys.Tap(Hwnd, KeyName.ToVk(r.Key), 15);
                    _lastFire[r] = Environment.TickCount64;
                    Report($"Pot {r.Key} @ {(r.UseSp ? "SP" : "HP")} {pct:0}%");
                }
            }
            await Timing.DelayAsync(40, ct);
        }
    }

    private void TryMouseboost()
    {
        // SmookyzAP-style: resetting a client counter removes the ~100ms item-use delay.
        var a = Session.AddressBook.Get("Mouseboost");
        if (a is not null) Session.Reader.WriteInt32(a.Resolve(Session.Reader.ModuleBase), 0);
    }
}
