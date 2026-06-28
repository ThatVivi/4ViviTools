using System.Text.Json;

namespace FourRVivi.Core.Trackers;

public sealed class MvpEntry
{
    public int MobId { get; set; }
    public string Name { get; set; } = "";
    public string Map { get; set; } = "";
    public int MinMinutes { get; set; } = 60;
    public int MaxMinutes { get; set; } = 70;
    public string DefElement { get; set; } = "";   // MVP's defensive element (for best-element advice)
    public DateTime? KilledAt { get; set; }

    public DateTime? NextMin => KilledAt?.AddMinutes(MinMinutes);
    public DateTime? NextMax => KilledAt?.AddMinutes(MaxMinutes);

    public string Status()
    {
        if (KilledAt is null) return "—";
        var now = DateTime.Now;
        if (now < NextMin) return "not yet · in " + Fmt(NextMin!.Value - now);
        if (now <= NextMax) return "maybe · window";
        return "spawned";
    }

    /// <summary>Status colour: grey (no timer), red (not yet), yellow (window), green (spawned).</summary>
    public string StatusColor()
    {
        if (KilledAt is null) return "#888888";
        var now = DateTime.Now;
        if (now < NextMin) return "#E5484D";
        if (now <= NextMax) return "#F5C24B";
        return "#5EC26A";
    }
    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m" : $"{t.Minutes}m{t.Seconds:00}s";
}

/// <summary>Manual MVP kill register + respawn math + due alerts. Persists to %AppData%/4rVivi/mvp.json.</summary>
public sealed class MvpTracker
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string Path { get; }
    public List<MvpEntry> Entries { get; private set; } = new();

    public MvpTracker()
    {
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "mvp.json");
        Load();
    }

    public void Load()
    {
        try { if (File.Exists(Path)) Entries = JsonSerializer.Deserialize<List<MvpEntry>>(File.ReadAllText(Path)) ?? new(); }
        catch { Entries = new(); }
    }
    public void Save() { try { File.WriteAllText(Path, JsonSerializer.Serialize(Entries, Opt)); } catch { } }

    public void RegisterKill(MvpEntry e) { e.KilledAt = DateTime.Now; Save(); }
    public IEnumerable<MvpEntry> DueSoon(int withinMinutes = 5) =>
        Entries.Where(e => e.NextMin is { } n && DateTime.Now >= n.AddMinutes(-withinMinutes) && DateTime.Now <= (e.NextMax ?? n));
}
