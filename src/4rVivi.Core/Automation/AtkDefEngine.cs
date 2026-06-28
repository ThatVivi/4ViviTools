using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>4RTools "ATK x DEF" mode: spam a skill key while switching between an ATK equip set and a
/// DEF equip set. Faithful to 4RTools/Forms/ATKDEFForm: ATK Switch key, DEF Switch key, Switch Delay,
/// Spammer Key, Spammer Delay, optional mouse click on the spam.</summary>
public sealed class AtkDefEngine : AutomationEngine
{
    private readonly MouseSender _mouse;

    public string AtkSwitchKey { get; set; } = "F5";
    public string DefSwitchKey { get; set; } = "F6";
    public int SwitchDelayMs { get; set; } = 500;
    public string SpammerKey { get; set; } = "F1";
    public override void ClearKeys() { AtkSwitchKey = ""; DefSwitchKey = ""; SpammerKey = ""; }
    public int SpammerDelayMs { get; set; } = 200;
    public bool WithMouseClick { get; set; }
    /// <summary>true = currently in ATK stance (spamming); false = DEF stance.</summary>
    public bool AtkMode { get; set; } = true;

    public AtkDefEngine(GameSession s, KeySender k, MouseSender mouse, HumanizedTiming t)
        : base("ATKxDEF", s, k, t) { _mouse = mouse; }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        bool lastMode = AtkMode;
        // press the matching switch key once at start
        PressSwitch(AtkMode);
        while (!ct.IsCancellationRequested)
        {
            bool active = Enabled && (Session.Reader.Attached || LiveStats.Instance.IsFresh);
            if (active)
            {
                if (AtkMode != lastMode)               // stance changed → press its switch key
                {
                    PressSwitch(AtkMode);
                    lastMode = AtkMode;
                    await Timing.DelayAsync(SwitchDelayMs, ct);
                    continue;
                }
                if (AtkMode)                            // ATK stance → spam
                {
                    if (!string.IsNullOrWhiteSpace(SpammerKey))
                        Keys.Tap(Hwnd, KeyName.ToVk(SpammerKey), 15);
                    if (WithMouseClick)
                    {
                        var (w, h) = _mouse.ClientSize(Hwnd);
                        if (w > 0 && h > 0) _mouse.Click(Hwnd, w / 2, h / 2);
                    }
                    await Timing.DelayAsync(SpammerDelayMs, ct);
                    continue;
                }
            }
            await Timing.DelayAsync(SpammerDelayMs, ct);
        }
    }

    private void PressSwitch(bool atk)
    {
        var key = atk ? AtkSwitchKey : DefSwitchKey;
        if (!string.IsNullOrWhiteSpace(key)) Keys.Tap(Hwnd, KeyName.ToVk(key), 15);
    }
}
