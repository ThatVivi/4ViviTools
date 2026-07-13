using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>One debuff -> cure mapping: when any of the keywords appears in on-screen status text
/// (from the auto-scan OCR), press the cure key.</summary>
public sealed class DebuffRule
{
    public string Name { get; set; } = "";                 // display (e.g. "Poison", "Stone Curse", "Frozen")
    public List<string> Keywords { get; } = new();         // status texts that indicate it (e.g. "Poison","Envenom")
    public string Key { get; set; } = "";                  // cure key (Panacea / Green Potion / cure skill)
    public int CooldownMs { get; set; } = 1200;
    public bool Enabled { get; set; } = true;
}

/// <summary>Auto-cure: reads on-screen status text from <see cref="LiveScene"/> and presses the
/// mapped cure key when a debuff keyword is detected. Vision-driven (no memory). Each rule has its
/// own cooldown so it doesn't spam-waste cure items.</summary>
public sealed class AutoDebuffEngine : AutomationEngine
{
    public List<DebuffRule> Rules { get; } = new();
    public override void ClearKeys() { foreach (var r in Rules) r.Key = ""; }
    private readonly Dictionary<DebuffRule, long> _lastFire = new();

    public AutoDebuffEngine(GameSession s, KeySender k, HumanizedTiming t) : base("AutoDebuff", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Enabled && LiveScene.Instance.IsFresh)
            {
                long now = Environment.TickCount64;
                foreach (var r in Rules.ToArray())
                {
                    if (!r.Enabled || string.IsNullOrWhiteSpace(r.Key) || r.Keywords.Count == 0) continue;
                    if (_lastFire.TryGetValue(r, out long last) && now - last < r.CooldownMs) continue;
                    bool present = false;
                    foreach (var kw in r.Keywords) if (LiveScene.Instance.HasStatus(kw)) { present = true; break; }
                    if (!present) continue;
                    Keys.Tap(Hwnd, KeyName.ToVk(r.Key), 15);
                    _lastFire[r] = Environment.TickCount64;
                    Report($"Cure {r.Name} -> {r.Key}");
                    await Timing.DelayAsync(120, ct);
                }
            }
            await Timing.DelayAsync(80, ct);
        }
    }
}
