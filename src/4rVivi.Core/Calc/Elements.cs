namespace FourRVivi.Core.Calc;

public enum Element { Neutral = 0, Water, Earth, Fire, Wind, Poison, Holy, Shadow, Ghost, Undead }

/// <summary>Element modifier table copied verbatim from rAthena db/re/attr_fix.yml.
/// Indexed by ATTACKER weapon element vs DEFENDER element, per defender element level (1-4).
/// Returns a fraction (1.00 = 100%). rAthena "Dark" == our Shadow. Column order =
/// {Neutral,Water,Earth,Fire,Wind,Poison,Holy,Shadow,Ghost,Undead}.</summary>
public static class Elements
{
    // t[attacker][level-1][defender] = percent
    private static readonly int[][][] T = Build();

    public static double Modifier(Element atk, Element def, int defLevel)
    {
        int lvl = defLevel < 1 ? 1 : defLevel > 4 ? 4 : defLevel;
        return T[(int)atk][lvl - 1][(int)def] / 100.0;
    }

    /// <summary>Parse a defender element name ("Water", "Dark", "Holy 2"…) to our enum.</summary>
    public static Element? TryParse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var token = s.Trim().Split(' ', '_', '/')[0].ToLowerInvariant();
        return token switch
        {
            "neutral" => Element.Neutral, "water" => Element.Water, "earth" => Element.Earth,
            "fire" => Element.Fire, "wind" => Element.Wind, "poison" => Element.Poison,
            "holy" => Element.Holy, "shadow" or "dark" => Element.Shadow,
            "ghost" => Element.Ghost, "undead" => Element.Undead, _ => null,
        };
    }

    /// <summary>The attack element that deals the most damage to this defender element/level.</summary>
    public static Element BestAttackElement(Element def, int defLevel = 1)
    {
        Element best = Element.Neutral; double bestMod = -999;
        foreach (Element atk in System.Enum.GetValues(typeof(Element)))
        {
            double m = Modifier(atk, def, defLevel);
            if (m > bestMod) { bestMod = m; best = atk; }
        }
        return best;
    }

    private static int[][][] Build() => new[]
    {
        // Neutral attacker
        new[]{ new[]{100,100,100,100,100,100,100,100, 90,100}, new[]{100,100,100,100,100,100,100,100, 70,100},
               new[]{100,100,100,100,100,100,100,100, 50,100}, new[]{100,100,100,100,100,100,100,100,  0,100} },
        // Water attacker
        new[]{ new[]{100, 25,100,150, 90,150,100,100,100,100}, new[]{100,  0,100,175, 80,150,100,100,100,100},
               new[]{100,  0,100,200, 70,125,100,100,100,100}, new[]{100,  0,100,200, 60,125,100,100,100,100} },
        // Earth attacker
        new[]{ new[]{100,100, 25, 90,150,150,100,100,100,100}, new[]{100,100,  0, 80,175,150,100,100,100,100},
               new[]{100,100,  0, 70,200,125,100,100,100,100}, new[]{100,100,  0, 60,200,125,100,100,100,100} },
        // Fire attacker
        new[]{ new[]{100, 90,150, 25,100,150,100,100,100,125}, new[]{100, 80,175,  0,100,150,100,100,100,150},
               new[]{100, 70,200,  0,100,125,100,100,100,175}, new[]{100, 60,200,  0,100,125,100,100,100,200} },
        // Wind attacker
        new[]{ new[]{100,150, 90,100, 25,150,100,100,100,100}, new[]{100,175, 80,100,  0,150,100,100,100,100},
               new[]{100,200, 70,100,  0,125,100,100,100,100}, new[]{100,200, 60,100,  0,125,100,100,100,100} },
        // Poison attacker
        new[]{ new[]{100,150,150,150,150,  0, 75, 75, 75, 75}, new[]{100,150,150,150,150,  0, 75, 75, 75, 50},
               new[]{100,125,125,125,125,  0, 50, 50, 50, 25}, new[]{100,125,125,125,125,  0, 50, 50, 50,  0} },
        // Holy attacker
        new[]{ new[]{100,100,100,100,100, 75,  0,125,100,125}, new[]{100,100,100,100,100, 75,  0,150,100,150},
               new[]{100,100,100,100,100, 50,  0,175,100,175}, new[]{100,100,100,100,100, 50,  0,200,100,200} },
        // Shadow attacker (rAthena "Dark")
        new[]{ new[]{100,100,100,100,100, 75,125,  0,100,  0}, new[]{100,100,100,100,100, 75,150,  0,100,  0},
               new[]{100,100,100,100,100, 50,175,  0,100,  0}, new[]{100,100,100,100,100, 50,200,  0,100,  0} },
        // Ghost attacker
        new[]{ new[]{ 90,100,100,100,100, 75, 90, 90,125,100}, new[]{ 70,100,100,100,100, 75, 80, 80,150,125},
               new[]{ 50,100,100,100,100, 50, 70, 70,175,150}, new[]{  0,100,100,100,100, 50, 60, 60,200,175} },
        // Undead attacker
        new[]{ new[]{100,100,100, 90,100, 75,125,  0,100,  0}, new[]{100,100,100, 80,100, 50,150,  0,125,  0},
               new[]{100,100,100, 70,100, 25,175,  0,150,  0}, new[]{100,100,100, 60,100,  0,200,  0,175,  0} },
    };
}
