using System;
using System.Collections.Generic;
using System.Linq;

namespace FourRVivi.Core.Calc;

/// <summary>Maps a calculator class name to the rAthena item_db `Jobs` archetypes that class inherits,
/// so gear pickers can be filtered to what a class can actually equip (pc_isequip job mask).
/// See docs/rathena/weapon-equip-per-class.md.</summary>
public static class ClassEquip
{
    // archetype keys used by item_db_equip.yml Jobs:
    //  Swordman, Knight, Crusader, Mage, Wizard, Sage, Archer, Hunter, BardDancer, Acolyte, Priest,
    //  Monk, Merchant, Blacksmith, Alchemist, Thief, Assassin, Rogue, Novice, SuperNovice, Gunslinger,
    //  Rebellion, Ninja, KagerouOboro, Taekwon, StarGladiator, SoulLinker, Summoner, Spirit_Handler, All

    /// <summary>Archetypes a class can equip from (keyword-driven; covers normal/baby/extended lines).</summary>
    public static HashSet<string> ArchetypesFor(string? className)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All" };
        if (string.IsNullOrWhiteSpace(className)) return set;
        var c = className.Replace("Baby ", "").Trim();

        void Add(params string[] a) { foreach (var x in a) set.Add(x); }
        bool Has(params string[] kw) => kw.Any(k => c.Contains(k, StringComparison.OrdinalIgnoreCase));

        // Swordman tree
        if (c.Equals("Swordman", StringComparison.OrdinalIgnoreCase)) Add("Swordman");
        else if (Has("Knight", "Dragon Knight", "Lord Knight", "Rune")) Add("Swordman", "Knight");
        else if (Has("Crusader", "Paladin", "Royal Guard", "Imperial Guard")) Add("Swordman", "Crusader");
        // Mage tree
        else if (c.Equals("Mage", StringComparison.OrdinalIgnoreCase)) Add("Mage");
        else if (Has("Wizard", "Warlock", "Archmage")) Add("Mage", "Wizard");
        else if (Has("Sage", "Professor", "Sorcerer", "Elemental Master")) Add("Mage", "Sage");
        // Archer tree
        else if (c.Equals("Archer", StringComparison.OrdinalIgnoreCase)) Add("Archer");
        else if (Has("Hunter", "Sniper", "Ranger", "Windhawk")) Add("Archer", "Hunter");
        else if (Has("Bard", "Clown", "Minstrel", "Troubadour", "Dancer", "Gypsy", "Wanderer", "Trouvere")) Add("Archer", "BardDancer");
        // Acolyte tree
        else if (c.Equals("Acolyte", StringComparison.OrdinalIgnoreCase)) Add("Acolyte");
        else if (Has("Priest", "Bishop", "Cardinal")) Add("Acolyte", "Priest");
        else if (Has("Monk", "Champion", "Sura", "Inquisitor")) Add("Acolyte", "Monk");
        // Merchant tree
        else if (c.Equals("Merchant", StringComparison.OrdinalIgnoreCase)) Add("Merchant");
        else if (Has("Blacksmith", "Whitesmith", "Mechanic", "Meister")) Add("Merchant", "Blacksmith");
        else if (Has("Alchemist", "Creator", "Genetic", "Biolo")) Add("Merchant", "Alchemist");
        // Thief tree
        else if (c.Equals("Thief", StringComparison.OrdinalIgnoreCase)) Add("Thief");
        else if (Has("Assassin", "Guillotine", "Shadow Cross")) Add("Thief", "Assassin");
        else if (Has("Rogue", "Stalker", "Shadow Chaser", "Abyss")) Add("Thief", "Rogue");
        // Extended
        else if (Has("Hyper Novice", "Super Novice")) Add("Novice", "SuperNovice");
        else if (Has("Novice")) Add("Novice");
        else if (Has("Rebellion", "Night Watch")) Add("Gunslinger", "Rebellion");
        else if (Has("Gunslinger")) Add("Gunslinger");
        else if (Has("Kagerou", "Oboro", "Shinkiro", "Shiranui")) Add("Ninja", "KagerouOboro");
        else if (Has("Ninja")) Add("Ninja");
        else if (Has("Star Emperor", "Sky Emperor", "Star Gladiator")) Add("Taekwon", "StarGladiator");
        else if (Has("Soul")) Add("Taekwon", "SoulLinker");
        else if (Has("Taekwon")) Add("Taekwon");
        else if (Has("Doram", "Summoner")) Add("Summoner");
        else if (Has("Spirit Handler")) Add("Summoner", "Spirit_Handler");

        return set;
    }

    /// <summary>True if an equip (its Jobs list) can be worn by the given class.</summary>
    public static bool CanEquip(IEnumerable<string>? equipJobs, string? className)
    {
        if (equipJobs == null) return true;                 // unknown = allow
        var jobs = equipJobs as ICollection<string> ?? equipJobs.ToList();
        if (jobs.Count == 0) return true;
        var arch = ArchetypesFor(className);
        return jobs.Any(j => arch.Contains(j));
    }
}
