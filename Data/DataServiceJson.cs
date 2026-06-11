// DataServiceJson.cs — JSON-backed game database for 4rVivi.
// Loads gamedata.json from (1) next to the exe, else (2) EMBEDDED resource inside the exe.
// So the Database tab works even when only the single exe is shipped. NO SQLite.
// .NET Framework 4.x. namespace _4rVivi.Data. MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public int RecommendedSpamDelayMs { get { return Math.Max(AfterCastDelayMs, CooldownMs); } }
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
        public string SubType { get { return ""; } }
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
            string json = LoadJson(path);
            _d = JsonConvert.DeserializeObject<GameData>(json) ?? new GameData();
            _mobById = _d.Mobs.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            _skillById = _d.Skills.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            _itemById = _d.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        }

        private static string LoadJson(string path)
        {
            // 1) explicit path or next to the exe
            string p = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamedata.json");
            if (File.Exists(p)) return File.ReadAllText(p);

            // 2) embedded resource inside the exe (RootNamespace = _4RTools)
            var asm = Assembly.GetExecutingAssembly();
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("gamedata.json", StringComparison.OrdinalIgnoreCase))
                {
                    using (var s = asm.GetManifestResourceStream(name))
                    using (var r = new StreamReader(s))
                        return r.ReadToEnd();
                }
            }
            throw new FileNotFoundException("gamedata.json not found beside exe or embedded in assembly.");
        }

        private static bool Has(string hay, string needle)
            => !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        public MobInfo GetMob(int id) { MobInfo m; return _mobById.TryGetValue(id, out m) ? m : null; }
        public List<MobInfo> SearchMobs(string q, int limit = 50)
            => _d.Mobs.Where(m => Has(m.Name, q) || Has(m.Aegis, q)).OrderBy(m => m.Level).Take(limit).ToList();
        public List<DropInfo> GetDrops(int mobId) { var m = GetMob(mobId); return m != null ? m.Drops : new List<DropInfo>(); }
        public List<KeyValuePair<MobInfo, int>> WhoDrops(string itemAegis, int limit = 30)
            => _d.Mobs.SelectMany(m => m.Drops
                       .Where(d => string.Equals(d.ItemAegis, itemAegis, StringComparison.OrdinalIgnoreCase))
                       .Select(d => new KeyValuePair<MobInfo, int>(m, d.Rate)))
                      .OrderByDescending(t => t.Value).Take(limit).ToList();

        public SkillInfo GetSkill(int id) { SkillInfo s; return _skillById.TryGetValue(id, out s) ? s : null; }
        public List<SkillInfo> SearchSkills(string q, int limit = 50)
            => _d.Skills.Where(s => Has(s.Name, q)).Take(limit).ToList();

        public ItemInfo GetItem(int id) { ItemInfo i; return _itemById.TryGetValue(id, out i) ? i : null; }
        public ItemInfo GetItemByAegis(string aegis)
            => _d.Items.FirstOrDefault(i => string.Equals(i.Aegis, aegis, StringComparison.OrdinalIgnoreCase));
        public List<ItemInfo> SearchItems(string q, int limit = 50)
            => _d.Items.Where(i => Has(i.Name, q) || Has(i.Aegis, q)).Take(limit).ToList();

        public List<string> SearchMaps(string q, int limit = 50)
            => _d.Maps.Where(m => Has(m, q)).Take(limit).ToList();

        public void Dispose() { }
    }
}
