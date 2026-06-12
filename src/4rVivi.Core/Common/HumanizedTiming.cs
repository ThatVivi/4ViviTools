namespace FourRVivi.Core.Common;

/// <summary>Adds human-like jitter to delays so timings are not robotically constant.</summary>
public sealed class HumanizedTiming
{
    private readonly Random _rng = new();
    public bool Enabled { get; set; } = true;
    public double JitterFraction { get; set; } = 0.18;

    public int Apply(int baseMs)
    {
        if (!Enabled || baseMs <= 0) return Math.Max(0, baseMs);
        double j = baseMs * JitterFraction;
        return Math.Max(0, (int)(baseMs + (_rng.NextDouble() * 2 - 1) * j));
    }

    public async Task DelayAsync(int baseMs, CancellationToken ct) => await Task.Delay(Apply(baseMs), ct);
}
