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
    public bool UseControllerButtons { get; set; }
    public Dictionary<string, string> ControllerKeyMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PotConfig, long> _lastFire = new();
    private readonly Dictionary<PotConfig, int> _lowConfirmations = new();
    private readonly MouseSender _mouse;

    public AutopotEngine(GameSession s, KeySender k, MouseSender mouse, HumanizedTiming t) : base("Autopot", s, k, t)
    {
        _mouse = mouse;
    }
    public override void ClearKeys() { foreach (var r in Rules) r.Key = ""; }

    protected override async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Enabled && (Session.Reader.Attached || FourRVivi.Core.Game.LiveStats.Instance.IsFresh))
            {
                double hp = Session.Health.HpPercent, sp = Session.Health.SpPercent;
                long now = Environment.TickCount64;

                foreach (var r in Rules)
                {
                    if (!r.Enabled) continue;
                    double pct = r.UseSp ? sp : hp;
                    if (pct <= 0 || pct > 100) continue;   // unknown/garbage read -> don't fire (anti-spam)
                    if (pct > r.Percent)
                    {
                        _lowConfirmations.Remove(r);
                        continue;
                    }
                    if (!LowReadConfirmed(r, pct)) continue;
                    int useDelay = ResolveUseDelayMs(r, pct);
                    if (_lastFire.TryGetValue(r, out long last) && now - last < useDelay) continue;

                    await Timing.DelayAsync(ResolveReactionMs(r, pct), ct);
                    if (Mouseboost) TryMouseboost();
                    TapAction(r.Key, 15);
                    _lastFire[r] = Environment.TickCount64;
                    var name = string.IsNullOrWhiteSpace(r.Name) ? r.Key : r.Name;
                    Report($"Pot {name} ({r.Key}) @ {(r.UseSp ? "SP" : "HP")} {pct:0}%");
                }
            }
            await Timing.DelayAsync(40, ct);
        }
    }

    private bool LowReadConfirmed(PotConfig rule, double pct)
    {
        if (pct <= 1) return true;
        _lowConfirmations.TryGetValue(rule, out var count);
        count++;
        _lowConfirmations[rule] = count;
        return count >= 2;
    }

    private int ResolveReactionMs(PotConfig rule, double pct)
    {
        if (rule.ReactionMs >= 0)
            return Math.Clamp(rule.ReactionMs, 0, 5000);

        double urgency = Math.Clamp((rule.Percent - pct) / Math.Max(1.0, rule.Percent), 0.0, 1.0);
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 18, 0, 100) : 140;
        int input = InputBackendPenaltyMs() + (UseControllerButtons ? 18 : 0);
        int hpSafety = rule.UseSp ? 25 : (pct <= Math.Max(5, rule.Percent / 2.0) ? -35 : 0);
        int auto = Math.Clamp((int)Math.Round(130 - 85 * urgency + stats + input + hpSafety), 15, 420);
        var learned = SmartBotTrainingTuning.Instance.Current;
        return SmartBotTrainingTuning.BlendAuto(auto, learned.PotReactionMs, TrainingWeight(learned), 15, 420);
    }

    private int ResolveUseDelayMs(PotConfig rule, double pct)
    {
        if (rule.UseDelayMs >= 0)
            return Math.Clamp(rule.UseDelayMs, 50, 60000);

        double urgency = Math.Clamp((rule.Percent - pct) / Math.Max(1.0, rule.Percent), 0.0, 1.0);
        int stats = StatsAgeMs() < 3000 ? Math.Clamp(StatsAgeMs() / 10, 0, 220) : 260;
        int input = InputBackendPenaltyMs() + (Mouseboost ? -30 : 35) + (UseControllerButtons ? 20 : 0);
        int baseDelay = rule.UseSp ? 560 : 480;
        int auto = Math.Clamp((int)Math.Round(baseDelay - 180 * urgency + stats + input), 120, 1600);
        var learned = SmartBotTrainingTuning.Instance.Current;
        return SmartBotTrainingTuning.BlendAuto(auto, learned.PotUseDelayMs, TrainingWeight(learned), 120, 1600);
    }

    private int StatsAgeMs()
        => LiveStats.Instance.UpdatedUtc == DateTime.MinValue
            ? 5000
            : (int)Math.Clamp((DateTime.UtcNow - LiveStats.Instance.UpdatedUtc).TotalMilliseconds, 0, 5000);

    private int InputBackendPenaltyMs()
        => Keys.Method switch
        {
            InputMethod.Viiper => 22,
            InputMethod.VirtualHid => 38,
            InputMethod.ReWasdClick => 55,
            InputMethod.SendInput => 65,
            InputMethod.MouseKeyEvent => 70,
            InputMethod.PostMessage => 95,
            _ => 75
        };

    private static double TrainingWeight(SmartBotTrainingTuning.Snapshot learned)
        => learned.SampleCount <= 0 ? 0.0 : Math.Clamp(learned.SampleCount / 50.0, 0.10, 0.45);

    private void TryMouseboost()
    {
        // SmookyzAP-style: resetting a client counter removes the ~100ms item-use delay.
        var a = Session.AddressBook.Get("Mouseboost");
        if (a is not null) Session.Reader.WriteInt32(a.Resolve(Session.Reader.ModuleBase), 0);
    }

    private void TapAction(string action, int holdMs)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        if (Keys.Method == InputMethod.VirtualHid && KeyName.ToVk(action) > 0)
        {
            int vk = KeyName.ToVk(action);
            DebugTrace.Write("Autopot", $"TapAction virtual HID key action='{action}' vk={vk} holdMs={holdMs}.");
            if (Keys.TryVirtualHidTap(Hwnd, vk, Math.Max(60, holdMs)))
                return;

            DebugTrace.Write("Autopot", $"TapAction virtual HID key failed for '{action}'; trying controller mapping then keyboard fallback.");
            if (UseControllerButtons)
            {
                var mappedButton = ResolveControllerButton(action);
                DebugTrace.Write("Autopot", $"TapAction controller action='{action}' resolved='{mappedButton}' holdMs={holdMs} mapCount={ControllerKeyMap.Count}.");
                if (!string.IsNullOrWhiteSpace(mappedButton))
                    _mouse.TapVirtualButton(mappedButton, holdMs);
            }

            if (Keys.FallbackToNormalKeyWhenVirtualHidFails || !_mouse.IsReWasdRunning())
            {
                if (!Keys.FallbackToNormalKeyWhenVirtualHidFails)
                    DebugTrace.Write("Autopot", $"Virtual/controller key bridge is not running; sending real key fallback for '{action}'.");
                Keys.TapSendInputFallback(Hwnd, vk, holdMs);
            }
            return;
        }
        if (!UseControllerButtons)
        {
            DebugTrace.Write("Autopot", $"TapAction keyboard action='{action}' vk={KeyName.ToVk(action)} holdMs={holdMs}.");
            Keys.Tap(Hwnd, KeyName.ToVk(action), holdMs);
            return;
        }

        var button = ResolveControllerButton(action);
        DebugTrace.Write("Autopot", $"TapAction controller action='{action}' resolved='{button}' holdMs={holdMs} mapCount={ControllerKeyMap.Count}.");
        if (!string.IsNullOrWhiteSpace(button))
            _mouse.TapVirtualButton(button, holdMs);
        else
            DebugTrace.Write("Autopot", $"TapAction dropped: no controller mapping for '{action}'.");

        if (!_mouse.IsReWasdRunning() && KeyName.ToVk(action) > 0)
        {
            DebugTrace.Write("Autopot", $"Controller bridge is not running; sending real key fallback for '{action}'.");
            Keys.TapSendInputFallback(Hwnd, KeyName.ToVk(action), holdMs);
        }
    }

    private string ResolveControllerButton(string action)
    {
        return ControllerKeyMap.TryGetValue(action, out var mapped) && ReWasdMouseMap.IsButtonChord(mapped)
            ? ReWasdMouseMap.NormalizeChord(mapped)
            : ReWasdMouseMap.NormalizeChord(action);
    }
}
