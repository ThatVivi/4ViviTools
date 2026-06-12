using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Spams one skill key, or cycles a rotation of keys, at a fixed delay.</summary>
public sealed class SkillSpamEngine : AutomationEngine
{
    public string Key { get; set; } = "F1";
    public int DelayMs { get; set; } = 200;
    public List<string> Rotation { get; } = new();
    private int _idx;

    public SkillSpamEngine(GameSession s, KeySender k, HumanizedTiming t) : base("Skills", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Session.MasterEnabled && Session.Reader.Attached)
            {
                string key = Rotation.Count > 0 ? Rotation[_idx++ % Rotation.Count] : Key;
                if (!string.IsNullOrWhiteSpace(key)) Keys.Tap(Hwnd, KeyName.ToVk(key), 15);
            }
            await Timing.DelayAsync(DelayMs, ct);
        }
    }
}
