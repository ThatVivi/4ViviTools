namespace FourRVivi.Core.Automation;

public sealed class SmartBotTrainingTuning
{
    public static SmartBotTrainingTuning Instance { get; } = new();

    private readonly object _lock = new();
    private Snapshot _snapshot = new();

    public Snapshot Current
    {
        get { lock (_lock) return _snapshot; }
    }

    public void Reset()
    {
        lock (_lock) _snapshot = new Snapshot();
    }

    public void Update(
        int? skillDelayMs = null,
        int? normalAttackDelayMs = null,
        int? walkWaitMs = null,
        int? teleportDelayMs = null,
        int? potReactionMs = null,
        int? potUseDelayMs = null,
        int sampleCount = 0)
    {
        lock (_lock)
        {
            _snapshot = _snapshot with
            {
                SkillDelayMs = ClampOrNull(skillDelayMs, 80, 5000, _snapshot.SkillDelayMs),
                NormalAttackDelayMs = ClampOrNull(normalAttackDelayMs, 60, 1500, _snapshot.NormalAttackDelayMs),
                WalkWaitMs = ClampOrNull(walkWaitMs, 450, 5000, _snapshot.WalkWaitMs),
                TeleportDelayMs = ClampOrNull(teleportDelayMs, 250, 9000, _snapshot.TeleportDelayMs),
                PotReactionMs = ClampOrNull(potReactionMs, 0, 2000, _snapshot.PotReactionMs),
                PotUseDelayMs = ClampOrNull(potUseDelayMs, 120, 6000, _snapshot.PotUseDelayMs),
                SampleCount = Math.Max(_snapshot.SampleCount, sampleCount),
                UpdatedUtc = DateTime.UtcNow,
            };
        }
    }

    public static int BlendAuto(int autoMs, int? learnedMs, double weight, int minMs, int maxMs)
    {
        if (!learnedMs.HasValue)
            return Math.Clamp(autoMs, minMs, maxMs);
        weight = Math.Clamp(weight, 0.0, 0.75);
        var mixed = (int)Math.Round(autoMs * (1.0 - weight) + learnedMs.Value * weight);
        return Math.Clamp(mixed, minMs, maxMs);
    }

    private static int? ClampOrNull(int? next, int min, int max, int? fallback)
        => next.HasValue ? Math.Clamp(next.Value, min, max) : fallback;

    public readonly record struct Snapshot(
        int? SkillDelayMs = null,
        int? NormalAttackDelayMs = null,
        int? WalkWaitMs = null,
        int? TeleportDelayMs = null,
        int? PotReactionMs = null,
        int? PotUseDelayMs = null,
        int SampleCount = 0,
        DateTime UpdatedUtc = default);
}
