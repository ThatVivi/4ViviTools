using System.Collections.Generic;
using System.Linq;

namespace FourRVivi.Core.Calc;

public enum ClassGroup { Normal, Baby, Extended }

public sealed record ClassEntry(string Name, ClassGroup Group);

/// <summary>Class list grouped Normal / Baby / Extended. The calculator filters the dropdown by the
/// group checkboxes the user ticks. Edit freely to match your server's roster.</summary>
public static class ClassCatalog
{
    public static readonly IReadOnlyList<ClassEntry> All = new List<ClassEntry>
    {
        // --- Normal (1st → 4th) ---
        new("Novice", ClassGroup.Normal),
        new("Swordman", ClassGroup.Normal), new("Knight", ClassGroup.Normal), new("Lord Knight", ClassGroup.Normal),
        new("Rune Knight", ClassGroup.Normal), new("Dragon Knight", ClassGroup.Normal),
        new("Crusader", ClassGroup.Normal), new("Paladin", ClassGroup.Normal), new("Royal Guard", ClassGroup.Normal), new("Imperial Guard", ClassGroup.Normal),
        new("Mage", ClassGroup.Normal), new("Wizard", ClassGroup.Normal), new("High Wizard", ClassGroup.Normal),
        new("Warlock", ClassGroup.Normal), new("Archmage", ClassGroup.Normal),
        new("Sage", ClassGroup.Normal), new("Professor", ClassGroup.Normal), new("Sorcerer", ClassGroup.Normal), new("Elemental Master", ClassGroup.Normal),
        new("Archer", ClassGroup.Normal), new("Hunter", ClassGroup.Normal), new("Sniper", ClassGroup.Normal),
        new("Ranger", ClassGroup.Normal), new("Windhawk", ClassGroup.Normal),
        new("Bard", ClassGroup.Normal), new("Clown", ClassGroup.Normal), new("Minstrel", ClassGroup.Normal), new("Troubadour", ClassGroup.Normal),
        new("Dancer", ClassGroup.Normal), new("Gypsy", ClassGroup.Normal), new("Wanderer", ClassGroup.Normal), new("Trouvere", ClassGroup.Normal),
        new("Acolyte", ClassGroup.Normal), new("Priest", ClassGroup.Normal), new("High Priest", ClassGroup.Normal),
        new("Arch Bishop", ClassGroup.Normal), new("Cardinal", ClassGroup.Normal),
        new("Monk", ClassGroup.Normal), new("Champion", ClassGroup.Normal), new("Sura", ClassGroup.Normal), new("Inquisitor", ClassGroup.Normal),
        new("Merchant", ClassGroup.Normal), new("Blacksmith", ClassGroup.Normal), new("Whitesmith", ClassGroup.Normal),
        new("Mechanic", ClassGroup.Normal), new("Meister", ClassGroup.Normal),
        new("Alchemist", ClassGroup.Normal), new("Creator", ClassGroup.Normal), new("Genetic", ClassGroup.Normal), new("Biolo", ClassGroup.Normal),
        new("Thief", ClassGroup.Normal), new("Assassin", ClassGroup.Normal), new("Assassin Cross", ClassGroup.Normal),
        new("Guillotine Cross", ClassGroup.Normal), new("Shadow Cross", ClassGroup.Normal),
        new("Rogue", ClassGroup.Normal), new("Stalker", ClassGroup.Normal), new("Shadow Chaser", ClassGroup.Normal), new("Abyss Chaser", ClassGroup.Normal),

        // --- Extended classes ---
        new("Super Novice", ClassGroup.Extended), new("Hyper Novice", ClassGroup.Extended),
        new("Gunslinger", ClassGroup.Extended), new("Rebellion", ClassGroup.Extended), new("Night Watch", ClassGroup.Extended),
        new("Ninja", ClassGroup.Extended), new("Kagerou", ClassGroup.Extended), new("Oboro", ClassGroup.Extended),
        new("Shinkiro", ClassGroup.Extended), new("Shiranui", ClassGroup.Extended),
        new("Taekwon", ClassGroup.Extended), new("Star Gladiator", ClassGroup.Extended), new("Star Emperor", ClassGroup.Extended), new("Sky Emperor", ClassGroup.Extended),
        new("Soul Linker", ClassGroup.Extended), new("Soul Reaper", ClassGroup.Extended), new("Soul Ascetic", ClassGroup.Extended),
        new("Doram (Summoner)", ClassGroup.Extended), new("Spirit Handler", ClassGroup.Extended),

        // --- Baby classes ---
        new("Baby Novice", ClassGroup.Baby),
        new("Baby Swordman", ClassGroup.Baby), new("Baby Rune Knight", ClassGroup.Baby), new("Baby Dragon Knight", ClassGroup.Baby),
        new("Baby Royal Guard", ClassGroup.Baby), new("Baby Imperial Guard", ClassGroup.Baby),
        new("Baby Warlock", ClassGroup.Baby), new("Baby Archmage", ClassGroup.Baby),
        new("Baby Sorcerer", ClassGroup.Baby), new("Baby Elemental Master", ClassGroup.Baby),
        new("Baby Ranger", ClassGroup.Baby), new("Baby Windhawk", ClassGroup.Baby),
        new("Baby Minstrel", ClassGroup.Baby), new("Baby Troubadour", ClassGroup.Baby),
        new("Baby Wanderer", ClassGroup.Baby), new("Baby Trouvere", ClassGroup.Baby),
        new("Baby Arch Bishop", ClassGroup.Baby), new("Baby Cardinal", ClassGroup.Baby),
        new("Baby Sura", ClassGroup.Baby), new("Baby Inquisitor", ClassGroup.Baby),
        new("Baby Mechanic", ClassGroup.Baby), new("Baby Meister", ClassGroup.Baby),
        new("Baby Genetic", ClassGroup.Baby), new("Baby Biolo", ClassGroup.Baby),
        new("Baby Guillotine Cross", ClassGroup.Baby), new("Baby Shadow Cross", ClassGroup.Baby),
        new("Baby Shadow Chaser", ClassGroup.Baby), new("Baby Abyss Chaser", ClassGroup.Baby),
        new("Baby Super Novice", ClassGroup.Baby),
    };

    /// <summary>Classes whose group is in the selected set (Normal/Baby/Extended).</summary>
    public static IEnumerable<string> Filter(bool normal, bool baby, bool extended)
        => All.Where(c => (normal && c.Group == ClassGroup.Normal)
                       || (baby && c.Group == ClassGroup.Baby)
                       || (extended && c.Group == ClassGroup.Extended))
              .Select(c => c.Name);
}
