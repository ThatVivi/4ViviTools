namespace FourRVivi.Core.Automation;

public enum BotLogKind { Info, Movement, Skill, Kill, Item, Ammo, Reconnect }

public readonly record struct BotLogEntry(DateTime Time, BotLogKind Kind, string Text)
{
    public string Stamp => Time.ToString("HH:mm:ss");
}

/// <summary>Shared rolling activity log for the Smart Bot (movement, skills, kills, loot, ammo,
/// reconnect). Pure data so Core writes it and the App binds it. Capped ring buffer.</summary>
public sealed class BotLog
{
    public static BotLog Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Queue<BotLogEntry> _items = new();
    public int Capacity { get; set; } = 300;
    public event Action<BotLogEntry>? Added;

    public void Add(BotLogKind kind, string text)
    {
        var e = new BotLogEntry(DateTime.Now, kind, text ?? "");
        lock (_lock) { _items.Enqueue(e); while (_items.Count > Capacity) _items.Dequeue(); }
        try { Added?.Invoke(e); } catch { }
    }

    public IReadOnlyList<BotLogEntry> Snapshot() { lock (_lock) return _items.ToArray(); }
    public void Clear() { lock (_lock) _items.Clear(); }
}
