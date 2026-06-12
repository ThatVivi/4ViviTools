using System.Text.Json;

namespace FourRVivi.Core.Trackers;

public sealed class LootRow { public string Time { get; set; } = ""; public string Item { get; set; } = ""; public int Qty { get; set; } = 1; }

/// <summary>Session loot notes. Manual entry (no inventory memory needed) + persistence.</summary>
public sealed class LootLog
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string Path { get; }
    public List<LootRow> Rows { get; private set; } = new();

    public LootLog()
    {
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "loot.json");
        try { if (File.Exists(Path)) Rows = JsonSerializer.Deserialize<List<LootRow>>(File.ReadAllText(Path)) ?? new(); } catch { }
    }
    public void Add(string item, int qty) { Rows.Insert(0, new LootRow { Time = DateTime.Now.ToString("HH:mm:ss"), Item = item, Qty = qty }); Save(); }
    public void Clear() { Rows.Clear(); Save(); }
    public void Save() { try { File.WriteAllText(Path, JsonSerializer.Serialize(Rows, Opt)); } catch { } }
}
