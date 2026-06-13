namespace FourRVivi.Core.Tools;

/// <summary>Base stats + level + weapon + aggregated gear bonuses. Pre-renewal-leaning, approximate.</summary>
public sealed class CalcInput
{
    public int BaseLevel = 99;
    public int Str, Agi, Vit, Int, Dex, Luk;
    public int WeaponAtk;
    public string WeaponType = "Sword";
    // aggregated gear/enchant/card bonuses
    public int AddStr, AddAgi, AddVit, AddInt, AddDex, AddLuk;
    public int AddAtk, AddMatk, AddDef, AddMdef, AddHit, AddFlee, AddCrit, AddAspdRate, AddMaxHP, AddMaxSP;
}

public static class StatCalculator
{
    // rough base ASPD ceiling per weapon class
    private static int BaseAspd(string w) => w switch
    {
        "Bare fist" => 50, "Dagger" => 50, "Sword" => 47, "Two-hand Sword" => 44,
        "Spear" => 41, "Two-hand Spear" => 41, "Axe" => 42, "Mace" => 45,
        "Staff" => 40, "Bow" => 46, "Katar" => 48, "Book" => 44, "Knuckle" => 45,
        "Instrument" => 42, "Whip" => 42, "Gun" => 48, _ => 46
    };

    public static Dictionary<string, int> Compute(CalcInput i)
    {
        int str = i.Str + i.AddStr, agi = i.Agi + i.AddAgi, vit = i.Vit + i.AddVit;
        int @int = i.Int + i.AddInt, dex = i.Dex + i.AddDex, luk = i.Luk + i.AddLuk;

        int atk = str + Sq(str / 10) + dex / 5 + luk / 5 + i.WeaponAtk + i.AddAtk;
        int matkMin = @int + Sq(@int / 7) + i.AddMatk;
        int matkMax = @int + Sq(@int / 5) + i.AddMatk;
        int hit = i.BaseLevel + dex + luk / 3 + i.AddHit;
        int flee = i.BaseLevel + agi + luk / 5 + i.AddFlee;
        int crit = (int)(luk * 0.3) + 1 + i.AddCrit;
        int def = vit + i.AddDef;
        int mdef = @int + i.AddMdef;
        int maxHp = 35 + i.BaseLevel * 5 + vit * 8 + i.AddMaxHP;
        int maxSp = 10 + i.BaseLevel * 2 + @int * 6 + i.AddMaxSP;

        // approximate ASPD
        double aspd = BaseAspd(i.WeaponType) + Math.Sqrt(agi * 4.0 + dex) * 1.4;
        aspd = aspd * (1 + i.AddAspdRate / 100.0);
        int aspdI = (int)Math.Min(193, 100 + aspd);

        return new()
        {
            ["ATK"] = atk, ["MATK min"] = matkMin, ["MATK max"] = matkMax,
            ["HIT"] = hit, ["FLEE"] = flee, ["CRIT"] = crit,
            ["DEF (soft)"] = def, ["MDEF (soft)"] = mdef,
            ["ASPD ~"] = aspdI, ["~Max HP"] = maxHp, ["~Max SP"] = maxSp,
            ["STR"] = str, ["AGI"] = agi, ["VIT"] = vit, ["INT"] = @int, ["DEX"] = dex, ["LUK"] = luk
        };
    }
    private static int Sq(int x) => x * x;
}
