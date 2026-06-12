namespace FourRVivi.Core.Trackers;

/// <summary>A named buff countdown the user starts (e.g. when re-casting). Shown on the HUD page.</summary>
public sealed class BuffTimer
{
    public string Name { get; set; } = "Buff";
    public int DurationSec { get; set; } = 120;
    public DateTime? StartedAt { get; set; }

    public void Start() => StartedAt = DateTime.Now;
    public int RemainingSec => StartedAt is { } s ? Math.Max(0, DurationSec - (int)(DateTime.Now - s).TotalSeconds) : 0;
    public bool Expired => StartedAt is not null && RemainingSec == 0;
    public string Display => StartedAt is null ? "idle" : (Expired ? "EXPIRED" : $"{RemainingSec}s");
}
