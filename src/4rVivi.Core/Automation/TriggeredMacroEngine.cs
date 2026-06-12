using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Plays named chain macros: on demand, or looped while master is ON.</summary>
public sealed class TriggeredMacroEngine : AutomationEngine
{
    public List<ChainMacro> Macros { get; } = new();
    private readonly Dictionary<ChainMacro, long> _last = new();

    public TriggeredMacroEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Macros", s, k, t) { }

    public async Task PlayAsync(ChainMacro m, CancellationToken ct = default)
    {
        foreach (var step in m.Steps)
        {
            ct.ThrowIfCancellationRequested();
            int vk = KeyName.ToVk(step.Key);
            if (vk != 0) Keys.Tap(Hwnd, vk, 15);
            await Task.Delay(Math.Max(20, step.GapMs), ct);
        }
        Report($"Macro '{m.Name}' played.");
    }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Session.MasterEnabled && Session.Reader.Attached)
            {
                long now = Environment.TickCount64;
                foreach (var m in Macros.ToArray())
                {
                    if (!m.LoopWhileOn) continue;
                    if (_last.TryGetValue(m, out long last) && now - last < m.IntervalMs) continue;
                    await PlayAsync(m, ct);
                    _last[m] = Environment.TickCount64;
                }
            }
            await Timing.DelayAsync(200, ct);
        }
    }
}
