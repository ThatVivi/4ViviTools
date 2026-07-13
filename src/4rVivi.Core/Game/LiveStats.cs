namespace FourRVivi.Core.Game;

public enum LiveStatQuality
{
    Trusted,
    Held,
    Suspect
}

public enum LiveStatSource
{
    Unknown,
    Ocr,
    BarFill, // Non-vital bars only. HP/SP safety consumers must use PercentText.
    PercentText,
    Memory,
    Cache,
    User
}

public sealed record LiveStatNumber(
    int Value,
    LiveStatSource Source,
    double Confidence,
    string RawText,
    DateTime UpdatedUtc,
    LiveStatQuality Quality);

/// <summary>Shared latest stats, written by the OCR loop and read by the top bar, Stats tab and Discord.
/// When Active and recent, consumers prefer this over memory (which Gepard may block).</summary>
public sealed class LiveStats
{
    public static LiveStats Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, int> _num = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveStatNumber> _numMeta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _txt = new(StringComparer.OrdinalIgnoreCase);
    public bool Active { get; set; }
    public DateTime UpdatedUtc { get; private set; } = DateTime.MinValue;

    public bool IsFresh => Active && (DateTime.UtcNow - UpdatedUtc).TotalSeconds < 3.0;

    public void Touch() { lock (_lock) { UpdatedUtc = DateTime.UtcNow; } }
    public void SetNumber(string role, int value)
        => SetNumber(role, value, LiveStatSource.Unknown, 1.0, value.ToString(), LiveStatQuality.Trusted);

    public void SetNumber(string role, int value, LiveStatSource source, double confidence, string rawText, LiveStatQuality quality)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _num[role] = value;
            _numMeta[role] = new LiveStatNumber(
                value,
                source,
                Math.Clamp(confidence, 0.0, 1.0),
                rawText ?? "",
                now,
                quality);
            UpdatedUtc = now;
        }
    }

    public void HoldNumber(string role, int value, LiveStatSource source, double confidence, string rawText)
        => SetNumber(role, value, source, confidence, rawText, LiveStatQuality.Held);

    public void SetText(string role, string value) { lock (_lock) { _txt[role] = value; UpdatedUtc = DateTime.UtcNow; } }

    public bool TryGetNumber(string role, out int value)
    {
        lock (_lock) { if (IsFresh && _num.TryGetValue(role, out value)) return true; }
        value = 0; return false;
    }

    public bool TryGetNumberMeta(string role, out LiveStatNumber stat)
    {
        lock (_lock)
        {
            if (IsFresh && _numMeta.TryGetValue(role, out stat!))
                return true;
        }
        stat = new LiveStatNumber(0, LiveStatSource.Unknown, 0, "", DateTime.MinValue, LiveStatQuality.Suspect);
        return false;
    }

    public bool TryGetTrustedNumber(string role, out int value, int maxAgeMs = 3000)
    {
        lock (_lock)
        {
            if (!IsFresh || !_numMeta.TryGetValue(role, out var stat))
            {
                value = 0;
                return false;
            }

            var ageMs = (DateTime.UtcNow - stat.UpdatedUtc).TotalMilliseconds;
            if (stat.Quality == LiveStatQuality.Trusted && ageMs <= maxAgeMs)
            {
                value = stat.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public string GetText(string role) { lock (_lock) { return IsFresh && _txt.TryGetValue(role, out var v) ? v : ""; } }

    public void Clear() { lock (_lock) { _num.Clear(); _numMeta.Clear(); _txt.Clear(); Active = false; UpdatedUtc = DateTime.MinValue; } }
}
