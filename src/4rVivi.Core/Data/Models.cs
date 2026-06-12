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
    [JsonPropertyName("size")] public string Size { get; set; } = "";
    [JsonPropertyName("baseExp")] public long BaseExp { get; set; }
    [JsonPropertyName("jobExp")] public long JobExp { get; set; }
    [JsonPropertyName("mvp")] public bool Mvp { get; set; }
    [JsonPropertyName("drops")] public List<DropInfo> Drops { get; set; } = new();
}
public sealed class SkillInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("castMs")] public int CastTimeMs { get; set; }
    [JsonPropertyName("delayMs")] public int AfterCastDelayMs { get; set; }
    [JsonPropertyName("cooldownMs")] public int CooldownMs { get; set; }
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
public sealed class GameData
{
    [JsonPropertyName("mobs")] public List<MobInfo> Mobs { get; set; } = new();
    [JsonPropertyName("skills")] public List<SkillInfo> Skills { get; set; } = new();
    [JsonPropertyName("items")] public List<ItemInfo> Items { get; set; } = new();
    [JsonPropertyName("maps")] public List<string> Maps { get; set; } = new();
}
