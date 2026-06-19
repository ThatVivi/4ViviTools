namespace FourRVivi.Core.Game;

/// <summary>Shared latest stats, written by the OCR loop and read by the top bar, Stats tab and Discord.
/// When Active and recent, consumers prefer this over memory (which Gepard may block).</summary>
public sealed class LiveStats
{
    public static LiveStats Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, int> _num = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _txt = new(StringComparer.OrdinalIgnoreCase);
    public bool Active { get; set; }
    public DateTime UpdatedUtc { get; private set; } = DateTime.MinValue;

    public bool IsFresh => Active && (DateTime.UtcNow - UpdatedUtc).TotalSeconds < 3.0;

    public void Touch() { lock (_lock) { UpdatedUtc = DateTime.UtcNow; } }
    public void SetNumber(string role, int value) { lock (_lock) { _num[role] = value; UpdatedUtc = DateTime.UtcNow; } }
    public void SetText(string role, string value) { lock (_lock) { _txt[role] = value; UpdatedUtc = DateTime.UtcNow; } }

    public bool TryGetNumber(string role, out int value)
    {
        lock (_lock) { if (IsFresh && _num.TryGetValue(role, out value)) return true; }
        value = 0; return false;
    }
    public string GetText(string role) { lock (_lock) { return IsFresh && _txt.TryGetValue(role, out var v) ? v : ""; } }

    public void Clear() { lock (_lock) { _num.Clear(); _txt.Clear(); Active = false; } }
}
