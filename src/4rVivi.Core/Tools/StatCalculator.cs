namespace FourRVivi.Core.Tools;

public sealed record CalcInput(int BaseLevel, int Str, int Agi, int Vit, int Int, int Dex, int Luk, int WeaponAtk);

/// <summary>Pre-renewal-style derived stats. Approximate; meant as a planning aid, not server-exact.</summary>
public static class StatCalculator
{
    public static Dictionary<string, int> Compute(CalcInput i)
    {
        int atkBase = i.Str + Sq(i.Str / 10) + i.Dex / 5 + i.Luk / 5;
        int atk = atkBase + i.WeaponAtk;
        int matkMin = i.Int + Sq(i.Int / 7);
        int matkMax = i.Int + Sq(i.Int / 5);
        int hit = i.BaseLevel + i.Dex + i.Luk / 3;
        int flee = i.BaseLevel + i.Agi + i.Luk / 5;
        int crit = (int)(i.Luk * 0.3) + 1;
        int softDef = i.Vit;
        int softMdef = i.Int;
        int maxHpRough = (35 + i.BaseLevel * 5) + i.Vit * 8;
        int maxSpRough = (10 + i.BaseLevel * 2) + i.Int * 6;

        return new()
        {
            ["ATK"] = atk, ["MATK min"] = matkMin, ["MATK max"] = matkMax,
            ["HIT"] = hit, ["FLEE"] = flee, ["CRIT"] = crit,
            ["Soft DEF"] = softDef, ["Soft MDEF"] = softMdef,
            ["~Max HP"] = maxHpRough, ["~Max SP"] = maxSpRough
        };
    }
    private static int Sq(int x) => x * x;
}
