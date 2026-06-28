using System.Text.Json.Serialization;

namespace FourRVivi.Core.Data;

public sealed class DropInfo
{
    [JsonPropertyName("item")] public string ItemAegis { get; set; } = "";
    [JsonPropertyName("rate")] public int Rate { get; set; }
}
public sealed class MobInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("aegis")] public string Aegis { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("hp")] public long Hp { get; set; }
    [JsonPropertyName("race")] public string Race { get; set; } = "";
    [JsonPropertyName("element")] public string Element { get; set; } = "";
    [JsonPropertyName("elementLevel")] public int ElementLevel { get; set; } = 1;
    [JsonPropertyName("size")] public string Size { get; set; } = "";
    [JsonPropertyName("baseExp")] public long BaseExp { get; set; }
    [JsonPropertyName("jobExp")] public long JobExp { get; set; }
    [JsonPropertyName("mvp")] public bool Mvp { get; set; }
    // Full stats (rAthena mob_db.yml) — used by the calculator's enemy auto-fill.
    [JsonPropertyName("atk")] public int Atk { get; set; }
    [JsonPropertyName("matk")] public int Matk { get; set; }
    [JsonPropertyName("def")] public int Def { get; set; }
    [JsonPropertyName("mdef")] public int Mdef { get; set; }
    [JsonPropertyName("str")] public int Str { get; set; }
    [JsonPropertyName("agi")] public int Agi { get; set; }
    [JsonPropertyName("vit")] public int Vit { get; set; }
    [JsonPropertyName("int")] public int Int { get; set; }
    [JsonPropertyName("dex")] public int Dex { get; set; }
    [JsonPropertyName("luk")] public int Luk { get; set; }
    [JsonPropertyName("drops")] public List<DropInfo> Drops { get; set; } = new();
}
public sealed class SkillInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("aegis")] public string Aegis { get; set; } = "";
    [JsonPropertyName("castMs")] public int CastTimeMs { get; set; }
    [JsonPropertyName("delayMs")] public int AfterCastDelayMs { get; set; }
    [JsonPropertyName("cooldownMs")] public int CooldownMs { get; set; }
    // From rAthena skill_db.yml (client skill catalog cross-referenced)
    [JsonPropertyName("hits")] public int Hits { get; set; } = 1;
    [JsonPropertyName("mult")] public double Multiplier { get; set; } = 1.0;   // ratio at max level
    [JsonPropertyName("mults")] public List<double> Mults { get; set; } = new();   // ratio per level (index 0 = Lv1)
    public int MaxLevel => Mults.Count > 0 ? Mults.Count : 1;
    public double MultAt(int level) => Mults.Count == 0 ? Multiplier : Mults[System.Math.Clamp(level, 1, Mults.Count) - 1];
    [JsonPropertyName("element")] public string Element { get; set; } = "Weapon";
    [JsonPropertyName("type")] public string Type { get; set; } = "";    // Weapon / Magic / Misc
    [JsonPropertyName("magic")] public bool Magic { get; set; }
    [JsonPropertyName("atk")] public bool Offensive { get; set; }
    public int RecommendedSpamDelayMs => Math.Max(AfterCastDelayMs, CooldownMs);
}
public sealed class ItemInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("aegis")] public string Aegis { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("slots")] public int Slots { get; set; }
    [JsonPropertyName("weight")] public int Weight { get; set; }
}
/// <summary>One parsed item-script bonus (from rAthena bonus/bonus2). Compact keys keep the data small.
/// Percent fields are whole numbers (20 = +20%). race/size/ele "All" = applies to any target.</summary>
public sealed class ModEntry
{
    [JsonPropertyName("s")] public int Str { get; set; }
    [JsonPropertyName("a")] public int Agi { get; set; }
    [JsonPropertyName("v")] public int Vit { get; set; }
    [JsonPropertyName("i")] public int Int { get; set; }
    [JsonPropertyName("d")] public int Dex { get; set; }
    [JsonPropertyName("l")] public int Luk { get; set; }
    [JsonPropertyName("atk")] public int Atk { get; set; }
    [JsonPropertyName("matk")] public int Matk { get; set; }
    [JsonPropertyName("atkp")] public int AtkPct { get; set; }
    [JsonPropertyName("matkp")] public int MatkPct { get; set; }
    [JsonPropertyName("hit")] public int Hit { get; set; }
    [JsonPropertyName("crit")] public int Crit { get; set; }
    [JsonPropertyName("racep")] public int RacePct { get; set; }
    [JsonPropertyName("race")] public string? Race { get; set; }
    [JsonPropertyName("sizep")] public int SizePct { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("elep")] public int ElePct { get; set; }
    [JsonPropertyName("ele")] public string? Ele { get; set; }
}

public sealed class EquipInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("aegis")] public string Aegis { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("slots")] public int Slots { get; set; }
    [JsonPropertyName("loc")] public List<string> Loc { get; set; } = new();
    [JsonPropertyName("subtype")] public string SubType { get; set; } = "";
    [JsonPropertyName("jobs")] public List<string> Jobs { get; set; } = new();   // archetypes that can equip (item_db Jobs)
    [JsonPropertyName("atk")] public int Atk { get; set; }
    [JsonPropertyName("matk")] public int Matk { get; set; }
    [JsonPropertyName("wlvl")] public int WeaponLevel { get; set; }
    [JsonPropertyName("def")] public int Def { get; set; }
    [JsonPropertyName("mods")] public List<ModEntry> Mods { get; set; } = new();
    [JsonPropertyName("bonuses")] public Dictionary<string,int> Bonuses { get; set; } = new();
    [JsonPropertyName("effect")] public string Effect { get; set; } = "";
}

public sealed class CardInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("loc")] public List<string> Loc { get; set; } = new();
    [JsonPropertyName("mods")] public List<ModEntry> Mods { get; set; } = new();
}

public sealed class EnchantInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mods")] public List<ModEntry> Mods { get; set; } = new();
}

/// <summary>Item combo (rAthena item_combos): if every item of any one set is equipped, apply the mods.</summary>
public sealed class ComboInfo
{
    [JsonPropertyName("sets")] public List<List<string>> Sets { get; set; } = new();
    [JsonPropertyName("mods")] public List<ModEntry> Mods { get; set; } = new();
}

public sealed class GameData
{
    [JsonPropertyName("mobs")] public List<MobInfo> Mobs { get; set; } = new();
    [JsonPropertyName("skills")] public List<SkillInfo> Skills { get; set; } = new();
    [JsonPropertyName("items")] public List<ItemInfo> Items { get; set; } = new();
    [JsonPropertyName("equips")] public List<EquipInfo> Equips { get; set; } = new();
    [JsonPropertyName("cards")] public List<CardInfo> Cards { get; set; } = new();
    [JsonPropertyName("enchants")] public List<EnchantInfo> Enchants { get; set; } = new();
    [JsonPropertyName("combos")] public List<ComboInfo> Combos { get; set; } = new();
    [JsonPropertyName("skillCatalog")] public Dictionary<string, List<string>> SkillCatalog { get; set; } = new();
    [JsonPropertyName("maps")] public List<string> Maps { get; set; } = new();
}
