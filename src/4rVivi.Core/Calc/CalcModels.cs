namespace FourRVivi.Core.Calc;

public enum CalcMode { Renewal, Classic, Fourth }
public enum Size { Small = 0, Medium, Large }
public enum Race { Formless = 0, Undead, Brute, Plant, Insect, Fish, Demon, DemiHuman, Angel, Dragon }
public enum WeaponClass { Melee, Ranged }   // ranged = bow/gun/instrument/whip

/// <summary>Primary stats + base level. Substats are derived by DamageCalculator.</summary>
public sealed class StatBlock
{
    public int BaseLevel = 1;
    public int Str, Agi, Vit, Int, Dex, Luk;
    // 4th-class trait stats (muhRO/rAthena): POW/STA/WIS/SPL/CON/CRT.
    public int Pow, Sta, Wis, Spl, Con, Crt;
}

/// <summary>Equipped weapon.</summary>
public sealed class Weapon
{
    public int BaseDamage;          // weapon ATK before refine/variance
    public int Level = 4;           // weapon level 1-4 (drives variance + refine bonus)
    public int Refine;              // +0..+20
    public Element Element = Element.Neutral;
    public WeaponClass Class = WeaponClass.Melee;
    public int FlatMatk;            // weapon MATK (renewal)
}

/// <summary>The thing being hit.</summary>
public sealed class Target
{
    public Element Element = Element.Neutral;
    public int ElementLevel = 1;    // 1-4
    public Size Size = Size.Medium;
    public Race Race = Race.Formless;
    public int HardDef;             // equipment/eDEF
    public int SoftDef;             // VIT-based flat reduction
    public int HardMdef;
    public int SoftMdef;
    public bool IsBoss;
}

/// <summary>One card or enchant = a bundle of additive bonuses. Stack as many as you like.</summary>
public sealed class GearBonus
{
    public string Name = "";
    public int Str, Agi, Vit, Int, Dex, Luk;   // flat stat adds
    public int FlatAtk;                          // EquipATK
    public int FlatMatk;
    public double AtkPercent;                    // ATK% (Group A multiplier), e.g. 0.10 = +10%
    public double RacePercent;                   // vs target race
    public double SizePercent;                   // vs target size
    public double ElementPercent;                // vs target element
    public double SkillPercent;                  // generic +% to the chosen skill
    public Race? RaceTarget;                     // race the RacePercent applies to (null = all)
    public Size? SizeTarget;
    public Element? ElementTarget;
}

/// <summary>A skill's damage profile (per mode). Multiplier is total (e.g. 5.00 = 500%).</summary>
public sealed class SkillProfile
{
    public string Name = "Basic Attack";
    public bool Magic;                 // true = MATK skill
    public double RenewalMultiplier = 1.0;
    public double ClassicMultiplier = 1.0;
    public int Hits = 1;
    public Element? ForcedElement;     // some skills force their own element (e.g. Holy)
}

public sealed class DamageResult
{
    public double Min, Avg, Max;
    public string Breakdown = "";
}
