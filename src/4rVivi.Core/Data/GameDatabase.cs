using System.Reflection;
using System.Text.Json;

namespace FourRVivi.Core.Data;

/// <summary>Loads gamedata.json from embedded resource (reliable) or beside the exe. Never throws.</summary>
public sealed class GameDatabase
{
    private readonly GameData _d;
    private Dictionary<string, List<MapMobSpawnInfo>>? _mapMobs;
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

    private string? LoadOptionalJson(string fileName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
                if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    using var s = asm.GetManifestResourceStream(name)!;
                    using var r = new StreamReader(s);
                    return r.ReadToEnd();
                }
            string beside = Path.Combine(AppContext.BaseDirectory, fileName);
            return File.Exists(beside) ? File.ReadAllText(beside) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Has(string h, string n) => !string.IsNullOrEmpty(h) && !string.IsNullOrEmpty(n) && h.Contains(n, StringComparison.OrdinalIgnoreCase);
    public static string NormalizeKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
    private static string Key(string? s) => NormalizeKey(s);

    private static string FriendlyToken(string? raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return "";
        var parts = raw.Replace("__", "_").Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1) return raw;
        return string.Join(" ", parts.Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
    }

    public List<MobInfo> SearchMobs(string q, int n = 60) => _d.Mobs.Where(m => Has(m.Name, q) || Has(m.Aegis, q)).OrderBy(m => m.Level).Take(n).ToList();
    public List<SkillInfo> SearchSkills(string q, int n = 60) => _d.Skills.Where(s => Has(s.Name, q) || Has(s.Aegis, q) || s.Id.ToString() == (q ?? "").Trim()).Take(n).ToList();
    public List<ItemInfo> SearchItems(string q, int n = 60) => _d.Items.Where(i => Has(i.Name, q) || Has(i.Aegis, q)).Take(n).ToList();
    public List<EquipInfo> SearchEquips(string q, string? slot = null, int n = 80)
        => _d.Equips.Where(e =>
               (string.IsNullOrEmpty(q) || Has(e.Name, q) || Has(e.Aegis, q)) &&
               (string.IsNullOrEmpty(slot) || e.Loc.Contains(slot, StringComparer.OrdinalIgnoreCase)))
           .Take(n).ToList();
    public EquipInfo? Equip(int id) => _d.Equips.FirstOrDefault(e => e.Id == id);
    public int EquipCount => _d.Equips.Count;

    // Full lists for dropdown pickers. NOTE: gear lives in Items (type Weapon/Armor/Card),
    // the Equips array is usually empty, so pickers read from Items by type.
    public IReadOnlyList<MobInfo> AllMobs() => _d.Mobs;
    public IReadOnlyList<SkillInfo> AllSkills() => _d.Skills;
    public IReadOnlyList<ItemInfo> AllItems() => _d.Items;
    public List<string> EnchantNames() =>
        _d.Enchants.Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();

    public EquipInfo? EquipByName(string name) =>
        _d.Equips.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    public CardInfo? CardByName(string name) =>
        _d.Cards.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    public EnchantInfo? EnchantByName(string name) =>
        _d.Enchants.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<ComboInfo> AllCombos() => _d.Combos;
    private Dictionary<string, SkillInfo>? _skillAliases;
    private Dictionary<string, ItemInfo>? _itemAliases;
    private Dictionary<string, MobInfo>? _mobAliases;

    public MobInfo? MobByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _mobAliases ??= BuildMobAliases();
        return _mobAliases.TryGetValue(Key(name), out var m) ? m : null;
    }

    public string MobDisplayName(string? value) => MobByName(value ?? "")?.Name ?? FriendlyToken(value);

    public string MonsterDisplayNameFromTrainingLabel(string? value)
    {
        var mob = MobByTrainingLabel(value);
        if (mob != null) return mob.Name;

        var token = MonsterTrainingToken(value);
        if (token.Length == 0) return "";
        return FriendlyToken(token);
    }

    public MobInfo? MobByTrainingLabel(string? value)
    {
        var token = MonsterTrainingToken(value);
        if (token.Length == 0) return null;
        foreach (var candidate in MonsterTrainingCandidates(token))
        {
            var mob = MobByName(candidate);
            if (mob != null) return mob;
        }
        return null;
    }

    public static string MonsterTrainingToken(string? value)
    {
        var label = (value ?? "").Trim();
        if (label.Length == 0) return "";

        if (label.Length > 0 && label[0] == '#')
        {
            int i = 1;
            while (i < label.Length && char.IsDigit(label[i])) i++;
            label = label.Substring(i).Trim();
            if (label.StartsWith("|", StringComparison.Ordinal)) label = label.Substring(1).Trim();
        }

        label = label.Replace('\\', '/');
        int slash = label.LastIndexOf('/');
        if (slash >= 0) label = label.Substring(slash + 1);
        int dot = label.LastIndexOf('.');
        if (dot > 0) label = label.Substring(0, dot);

        foreach (var prefix in new[]
        {
            "mob__", "monster__", "monsters__", "spr_monsters__", "sprite_monsters__",
            "sprite__", "sprites__", "spr__", "spr_"
        })
        {
            if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(prefix.Length);
                break;
            }
        }

        return label.Trim('_', ' ', '-', '\t');
    }

    private static IEnumerable<string> MonsterTrainingCandidates(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) yield break;
        token = token.Trim();

        string? strippedFrame = StripTrailingFrameToken(token);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ExpandMonsterCandidate(token, includeNumericIds: false)
                     .Concat(ExpandMonsterCandidate(strippedFrame, includeNumericIds: false))
                     .Concat(ExpandMonsterCandidate(token, includeNumericIds: true))
                     .Concat(ExpandMonsterCandidate(strippedFrame, includeNumericIds: true)))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                yield return candidate;
        }
    }

    private static IEnumerable<string> ExpandMonsterCandidate(string? token, bool includeNumericIds)
    {
        if (string.IsNullOrWhiteSpace(token)) yield break;
        token = token.Trim();
        if (!includeNumericIds)
        {
            yield return token;
            yield return token.Replace('_', ' ');
            yield return token.Replace("_", "");
            if (token.StartsWith("G_", StringComparison.OrdinalIgnoreCase))
                yield return token.Substring(2);
        }

        var parts = token.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (includeNumericIds)
        {
            foreach (var part in parts)
                if (int.TryParse(part, out var id))
                    yield return id.ToString();
        }
        for (int i = 1; i < parts.Length; i++)
            yield return string.Join("_", parts.Skip(i));
    }

    private static string? StripTrailingFrameToken(string token)
    {
        int underscore = token.LastIndexOf('_');
        if (underscore <= 0 || underscore == token.Length - 1) return null;
        var suffix = token.Substring(underscore + 1);
        if (suffix.Length is >= 1 and <= 3 && suffix.All(char.IsDigit))
            return token.Substring(0, underscore);
        return null;
    }

    public IReadOnlyList<MapMobSpawnInfo> MapMonsterSpawns(string map)
    {
        if (string.IsNullOrWhiteSpace(map)) return Array.Empty<MapMobSpawnInfo>();
        _mapMobs ??= LoadMapMobs();
        if (!_mapMobs.TryGetValue(map.Trim(), out var direct))
        {
            var match = _mapMobs.FirstOrDefault(kv =>
                string.Equals(kv.Key, map.Trim(), StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains(map.Trim(), StringComparison.OrdinalIgnoreCase) ||
                map.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
            direct = match.Value;
        }
        return direct is { Count: > 0 }
            ? direct.OrderByDescending(m => m.Amount).ThenBy(m => m.Name).ToList()
            : Array.Empty<MapMobSpawnInfo>();
    }

    private Dictionary<string, List<MapMobSpawnInfo>> LoadMapMobs()
    {
        var raw = LoadOptionalJson("map_mobs.json");
        if (string.IsNullOrWhiteSpace(raw))
            return new Dictionary<string, List<MapMobSpawnInfo>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<MapMobSpawnInfo>>>(raw)
                         ?? new Dictionary<string, List<MapMobSpawnInfo>>();
            var result = new Dictionary<string, List<MapMobSpawnInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (map, spawns) in parsed)
            {
                var merged = spawns
                    .Where(s => !string.IsNullOrWhiteSpace(s.Aegis))
                    .GroupBy(s => Key(s.Aegis))
                    .Select(g =>
                    {
                        var first = g.First();
                        var name = MobDisplayName(first.Aegis);
                        return new MapMobSpawnInfo
                        {
                            Aegis = first.Aegis,
                            Amount = g.Sum(s => Math.Max(1, s.Amount)),
                            Name = string.IsNullOrWhiteSpace(name) ? FriendlyToken(first.Aegis) : name
                        };
                    })
                    .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                    .OrderByDescending(s => s.Amount)
                    .ThenBy(s => s.Name)
                    .ToList();
                if (merged.Count > 0)
                    result[map] = merged;
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, List<MapMobSpawnInfo>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public List<string> MobDisplayNames() =>
        _d.Mobs.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    public SkillInfo? SkillByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _skillAliases ??= BuildSkillAliases();
        return _skillAliases.TryGetValue(Key(name), out var s) ? s : null;
    }

    public ItemInfo? ItemByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _itemAliases ??= BuildItemAliases();
        return _itemAliases.TryGetValue(Key(name), out var i) ? i : null;
    }

    public string SkillDisplayName(string? value) => SkillByName(value ?? "")?.Name ?? FriendlyToken(value);
    public string ItemDisplayName(string? value) => ItemByName(value ?? "")?.Name ?? FriendlyToken(value);

    public List<string> SkillDisplayNames() =>
        _d.Skills.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    private Dictionary<string, SkillInfo> BuildSkillAliases()
    {
        var map = new Dictionary<string, SkillInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string? alias, SkillInfo s)
        {
            var k = Key(alias);
            if (k.Length > 0 && !map.ContainsKey(k)) map[k] = s;
        }

        foreach (var s in _d.Skills.OrderBy(s => s.Id))
        {
            Add(s.Name, s);
            Add(s.Aegis, s);
            Add(s.Id.ToString(), s);
            if (!string.IsNullOrWhiteSpace(s.Aegis)) Add(s.Aegis.Replace("_", ""), s);
        }
        return map;
    }

    private Dictionary<string, MobInfo> BuildMobAliases()
    {
        var map = new Dictionary<string, MobInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string? alias, MobInfo m)
        {
            var k = Key(alias);
            if (k.Length > 0 && !map.ContainsKey(k)) map[k] = m;
        }

        foreach (var m in _d.Mobs.OrderBy(m => m.Id))
        {
            Add(m.Name, m);
            Add(m.Aegis, m);
            Add(m.Id.ToString(), m);
            if (!string.IsNullOrWhiteSpace(m.Aegis))
            {
                Add(m.Aegis.Replace("_", ""), m);
                if (m.Aegis.StartsWith("G_", StringComparison.OrdinalIgnoreCase))
                    Add(m.Aegis.Substring(2), m);
            }
        }
        return map;
    }

    private Dictionary<string, ItemInfo> BuildItemAliases()
    {
        var map = new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string? alias, ItemInfo i)
        {
            var k = Key(alias);
            if (k.Length > 0 && !map.ContainsKey(k)) map[k] = i;
        }

        foreach (var i in _d.Items.OrderBy(i => i.Id))
        {
            Add(i.Name, i);
            Add(i.Aegis, i);
            Add(i.Id.ToString(), i);
            if (!string.IsNullOrWhiteSpace(i.Aegis)) Add(i.Aegis.Replace("_", ""), i);
        }
        return map;
    }
    /// <summary>Full skill list for a class (rAthena skill_tree.yml with inheritance resolved).
    /// Catalog is keyed by a normalized token; this normalizes the class name to match.</summary>
    public List<string> SkillsForClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return new List<string>();
        var tok = NormalizeClass(className);
        if (tok.StartsWith("baby")) tok = tok.Substring(4);          // baby shares the normal tree
        if (tok == "doramsummoner" || tok == "doram") tok = "summoner";
        return _d.SkillCatalog.TryGetValue(tok, out var list) ? list : new List<string>();
    }
    private static string NormalizeClass(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private Dictionary<string, int>? _nameToId;
    /// <summary>Item id for a display name (for resolving icons by name). 0 if unknown.</summary>
    public int IconId(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var item = ItemByName(name);
        if (item != null) return item.Id;
        _nameToId ??= BuildNameIndex();
        return _nameToId.TryGetValue(name.Trim().ToLowerInvariant(), out var id) ? id : 0;
    }
    private Dictionary<string, int> BuildNameIndex()
    {
        var map = new Dictionary<string, int>();
        foreach (var i in _d.Items) { var k = i.Name.Trim().ToLowerInvariant(); if (k.Length > 0 && !map.ContainsKey(k)) map[k] = i.Id; }
        foreach (var e in _d.Equips) { var k = e.Name.Trim().ToLowerInvariant(); if (k.Length > 0 && !map.ContainsKey(k)) map[k] = e.Id; }
        return map;
    }
    public List<string> ItemNamesByType(params string[] types) =>
        _d.Items.Where(i => types.Any(t => i.Type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    public IReadOnlyList<MobInfo> MvpMobs() => _d.Mobs.Where(m => m.Mvp).OrderBy(m => m.Name).ToList();
    public MobInfo? Mob(int id) => _d.Mobs.FirstOrDefault(m => m.Id == id);
    public (int mobs,int skills,int items,int maps) Counts() => (_d.Mobs.Count,_d.Skills.Count,_d.Items.Count,SearchMaps("", int.MaxValue).Count);

    public List<string> SearchMaps(string q, int n = 60)
    {
        var query = (q ?? "").Trim();
        var maps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _d.Maps)
            if (!string.IsNullOrWhiteSpace(m))
                maps.Add(m);
        try
        {
            _mapMobs ??= LoadMapMobs();
            foreach (var m in _mapMobs.Keys)
                if (!string.IsNullOrWhiteSpace(m))
                    maps.Add(m);
        }
        catch { }
        foreach (var m in LoadRuntimeMapNames())
            if (!string.IsNullOrWhiteSpace(m))
                maps.Add(m);

        return maps
            .Where(m => query.Length == 0 || Has(m, query))
            .Take(n)
            .ToList();
    }

    private static IEnumerable<string> LoadRuntimeMapNames()
    {
        try
        {
            string bd = AppContext.BaseDirectory;
            foreach (var d in new[]
            {
                Path.Combine(bd, "OcrServer", "models", "icons"),
                Path.Combine(bd, "models", "icons"),
            })
            {
                var jf = Path.Combine(d, "map_names.json");
                if (!File.Exists(jf)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(jf));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    yield return prop.Name;
                    var display = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(display))
                        yield return display!;
                }
                yield break;
            }
        }
        finally { }
    }
}
