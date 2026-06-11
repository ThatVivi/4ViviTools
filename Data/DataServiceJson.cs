// DataServiceJson.cs — JSON-backed game database for 4rVivi (browser-build friendly).
// Reads gamedata.json with Newtonsoft.Json (already bundled in 4RTools) — NO SQLite,
// NO native DLLs, NO new NuGet packages. Same public API as the SQLite DataService.cs,
// so MainForm works unchanged.
//
// USE THIS *INSTEAD OF* DataService.cs for the browser/GitHub build.
// Do NOT include both files (duplicate type names). .NET Framework 4.x. MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace _4rVivi.Data
{
    public class DropInfo
    {
        [JsonProperty("item")] public string ItemAegis;
        [JsonProperty("rate")] public int Rate;
        [JsonProperty("kind")] public string Kind;
    }

    public class MobInfo
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("aegis")] public string Aegis;
        [JsonProperty("name")] public string Name;
        [JsonProperty("level")] public int Level;
        [JsonProperty("hp")] public long Hp;
        [JsonProperty("race")] public string Race;
        [JsonProperty("element")] public string Element;
        [JsonProperty("size")] public string Size;
        [JsonProperty("atk")] public int AtkMin;
        [JsonProperty("def")] public int Def;
        [JsonProperty("baseExp")] public long BaseExp;
        [JsonProperty("jobExp")] public long JobExp;
        [JsonProperty("drops")] public List<DropInfo> Drops = new List<DropInfo>();
    }

    public class SkillInfo
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("maxLv")] public int MaxLevel;
        [JsonProperty("castMs")] public int CastTimeMs;
        [JsonProperty("delayMs")] public int AfterCastDelayMs;
        [JsonProperty("cooldownMs")] public int CooldownMs;
        [JsonProperty("type")] public string Type;
        [JsonProperty("range")] public string Range;
        [JsonProperty("element")] public string Element;
        public int RecommendedSpamDelayMs => Math.Max(AfterCastDelayMs, CooldownMs);
    }

    public class ItemInfo
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("aegis")] public string Aegis;
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("slots")] public int Slots;
        [JsonProperty("weight")] public int Weight;
        [JsonProperty("buy")] public int Buy;
        public string SubType => "";
    }

    internal class GameData
    {
        [JsonProperty("mobs")] public List<MobInfo> Mobs = new List<MobInfo>();
        [JsonProperty("skills")] public List<SkillInfo> Skills = new List<SkillInfo>();
        [JsonProperty("items")] public List<ItemInfo> Items = new List<ItemInfo>();
        [JsonProperty("maps")] public List<string> Maps = new List<string>();
    }

    public sealed class DataService : IDisposable
    {
        private readonly GameData _d;
        private readonly Dictionary<int, MobInfo> _mobById;
        private readonly Dictionary<int, SkillInfo> _skillById;
        private readonly Dictionary<int, ItemInfo> _itemById;

        public DataService(string path = null)
        {
            path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamedata.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("gamedata.json not found. Ship it next to the exe.", path);
            _d = JsonConvert.DeserializeObject<GameData>(File.ReadAllText(path)) ?? new GameData();
            _mobById = _d.Mobs.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            _skillById = _d.Skills.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            _itemById = _d.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        }

        private static bool Has(string hay, string needle)
            => !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // ---- mobs ----
        public MobInfo GetMob(int id) => _mobById.TryGetValue(id, out var m) ? m : null;
        public List<MobInfo> SearchMobs(string q, int limit = 50)
            => _d.Mobs.Where(m => Has(m.Name, q) || Has(m.Aegis, q)).OrderBy(m => m.Level).Take(limit).ToList();
        public List<DropInfo> GetDrops(int mobId) => GetMob(mobId)?.Drops ?? new List<DropInfo>();
        public List<(MobInfo mob, int rate)> WhoDrops(string itemAegis, int limit = 30)
            => _d.Mobs.SelectMany(m => m.Drops.Where(d => string.Equals(d.ItemAegis, itemAegis, StringComparison.OrdinalIgnoreCase))
                                              .Select(d => (m, d.Rate)))
                      .OrderByDescending(t => t.Rate).Take(limit).ToList();

        // ---- skills ----
        public SkillInfo GetSkill(int id) => _skillById.TryGetValue(id, out var s) ? s : null;
        public List<SkillInfo> SearchSkills(string q, int limit = 50)
            => _d.Skills.Where(s => Has(s.Name, q)).Take(limit).ToList();

        // ---- items ----
        public ItemInfo GetItem(int id) => _itemById.TryGetValue(id, out var i) ? i : null;
        public ItemInfo GetItemByAegis(string aegis)
            => _d.Items.FirstOrDefault(i => string.Equals(i.Aegis, aegis, StringComparison.OrdinalIgnoreCase));
        public List<ItemInfo> SearchItems(string q, int limit = 50)
            => _d.Items.Where(i => Has(i.Name, q) || Has(i.Aegis, q)).Take(limit).ToList();

        // ---- maps ----
        public List<string> SearchMaps(string q, int limit = 50)
            => _d.Maps.Where(m => Has(m, q)).Take(limit).ToList();

        public void Dispose() { }
    }
}
