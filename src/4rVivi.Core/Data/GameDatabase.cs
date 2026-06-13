using System.Reflection;
using System.Text.Json;

namespace FourRVivi.Core.Data;

/// <summary>Loads gamedata.json from embedded resource (reliable) or beside the exe. Never throws.</summary>
public sealed class GameDatabase
{
    private readonly GameData _d;
    public string? LoadError { get; private set; }
    public string? LoadSource { get; private set; }
    public bool IsLoaded => LoadError is null && _d.Mobs.Count + _d.Items.Count + _d.Skills.Count > 0;

    public GameDatabase()
    {
        string? json = null;
        try { json = Load(); } catch (Exception ex) { LoadError = ex.Message; }
        try { _d = string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<GameData>(json) ?? new(); }
        catch (Exception ex) { LoadError = "gamedata.json unreadable: " + ex.Message; _d = new(); }
        if (LoadError is null && !IsLoaded) LoadError = "gamedata.json loaded but empty.";
    }

    public string Diagnostics() =>
        $"Source: {LoadSource ?? "(none)"} | Mobs {_d.Mobs.Count}, Items {_d.Items.Count}, Skills {_d.Skills.Count}"
        + (LoadError is null ? "" : $" | Error: {LoadError}");

    private string Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
            if (name.EndsWith("gamedata.json", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.GetManifestResourceStream(name)!;
                using var r = new StreamReader(s);
                LoadSource = "embedded";
                return r.ReadToEnd();
            }
        string beside = Path.Combine(AppContext.BaseDirectory, "gamedata.json");
        if (File.Exists(beside)) { LoadSource = beside; return File.ReadAllText(beside); }
        throw new FileNotFoundException("gamedata.json not embedded and not found beside the exe.");
    }

    private static bool Has(string h, string n) => !string.IsNullOrEmpty(h) && h.Contains(n, StringComparison.OrdinalIgnoreCase);
    public List<MobInfo> SearchMobs(string q, int n = 60) => _d.Mobs.Where(m => Has(m.Name, q) || Has(m.Aegis, q)).OrderBy(m => m.Level).Take(n).ToList();
    public List<SkillInfo> SearchSkills(string q, int n = 60) => _d.Skills.Where(s => Has(s.Name, q)).Take(n).ToList();
    public List<ItemInfo> SearchItems(string q, int n = 60) => _d.Items.Where(i => Has(i.Name, q) || Has(i.Aegis, q)).Take(n).ToList();
    public List<EquipInfo> SearchEquips(string q, string? slot = null, int n = 80)
        => _d.Equips.Where(e =>
               (string.IsNullOrEmpty(q) || Has(e.Name, q) || Has(e.Aegis, q)) &&
               (string.IsNullOrEmpty(slot) || e.Loc.Contains(slot, StringComparer.OrdinalIgnoreCase)))
           .Take(n).ToList();
    public EquipInfo? Equip(int id) => _d.Equips.FirstOrDefault(e => e.Id == id);
    public int EquipCount => _d.Equips.Count;

    public IReadOnlyList<MobInfo> MvpMobs() => _d.Mobs.Where(m => m.Mvp).OrderBy(m => m.Name).ToList();
    public MobInfo? Mob(int id) => _d.Mobs.FirstOrDefault(m => m.Id == id);
    public (int mobs,int skills,int items,int maps) Counts() => (_d.Mobs.Count,_d.Skills.Count,_d.Items.Count,_d.Maps.Count);
    public List<string> SearchMaps(string q, int n = 60) => _d.Maps.Where(m => Has(m, q)).Take(n).ToList();
}
