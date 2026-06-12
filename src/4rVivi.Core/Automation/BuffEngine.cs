using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>One periodic buff key + interval (mutable so the UI can edit it live).</summary>
public sealed class BuffRule
{
    public string Key { get; set; } = "F1";
    public int IntervalMs { get; set; } = 30000;
    public bool Enabled { get; set; } = true;
}

/// <summary>Periodically re-casts buff keys at fixed intervals (skill buffs, item buffs).</summary>
public sealed class BuffEngine : AutomationEngine
{
    public List<BuffRule> Rules { get; } = new();
    private readonly Dictionary<BuffRule, long> _last = new();

    public BuffEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Buffs", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Session.MasterEnabled && Session.Reader.Attached)
            {
                long now = Environment.TickCount64;
                foreach (var r in Rules.ToArray())
                {
                    if (!r.Enabled || string.IsNullOrWhiteSpace(r.Key) || r.IntervalMs <= 0) continue;
                    if (_last.TryGetValue(r, out long last) && now - last < r.IntervalMs) continue;
                    Keys.Tap(Hwnd, KeyName.ToVk(r.Key), 15);
                    _last[r] = Environment.TickCount64;
                    Report($"Buff {r.Key}");
                    await Timing.DelayAsync(120, ct);
                }
            }
            await Timing.DelayAsync(200, ct);
        }
    }
}
