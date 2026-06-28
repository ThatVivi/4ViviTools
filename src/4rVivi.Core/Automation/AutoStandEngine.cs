using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Auto-Stand (4RTools "Enable Auto Stand when forced to sit"), OCR-driven.
/// Two configurable triggers (both off-by-default-safe):
///  • Posture text box: if an OCR text region (role "Posture"/"State") reads "sit", press Stand.
///  • Motion dwell (opt-in): if the character-motion box stays at/below a threshold for a dwell, press Stand.
/// The Stand key should be the client's sit/stand toggle (default Insert) or a "/stand" alias key.</summary>
public sealed class AutoStandEngine : AutomationEngine
{
    public string StandKey { get; set; } = "Insert";
    public override void ClearKeys() { StandKey = ""; }
    public bool UseMotion { get; set; }                 // opt-in; may mis-trigger on plain idle
    public int MotionThreshold { get; set; } = 2;       // motion <= this = static sprite
    public int DwellMs { get; set; } = 2000;            // must stay static this long
    public int CooldownMs { get; set; } = 3000;        // wait after standing

    private long _idleMs;

    public AutoStandEngine(GameSession s, KeySender k, HumanizedTiming t) : base("AutoStand", s, k, t) { }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        const int tick = 250;
        while (!ct.IsCancellationRequested)
        {
            if (Enabled && LiveStats.Instance.IsFresh)
            {
                bool stand = false;

                // 1) Posture text says "sit"
                var posture = LiveStats.Instance.GetText("Posture");
                if (string.IsNullOrEmpty(posture)) posture = LiveStats.Instance.GetText("State");
                if (!string.IsNullOrEmpty(posture) &&
                    posture.Contains("sit", System.StringComparison.OrdinalIgnoreCase))
                    stand = true;

                // 2) Motion dwell (opt-in)
                if (!stand && UseMotion && LiveStats.Instance.TryGetNumber("CharMotion", out var m))
                {
                    if (m <= MotionThreshold) { _idleMs += tick; if (_idleMs >= DwellMs) stand = true; }
                    else _idleMs = 0;
                }

                if (stand)
                {
                    if (!string.IsNullOrWhiteSpace(StandKey)) Keys.Tap(Hwnd, KeyName.ToVk(StandKey), 15);
                    _idleMs = 0;
                    await Timing.DelayAsync(CooldownMs, ct);
                    continue;
                }
            }
            await Timing.DelayAsync(tick, ct);
        }
    }
}
