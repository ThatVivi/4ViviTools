using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FourRVivi.Core.Calc;

/// <summary>RO damage engine (classic + renewal), following rAthena/irowiki formulas.
/// Cards/enchants are summed from a GearBonus list so their stats apply immediately.
/// Includes a sensitivity-based advisor that tells the user what to focus on next.</summary>
public sealed class DamageCalculator
{
    public CalcMode Mode = CalcMode.Renewal;

    // Crit (renewal): base crit damage 140%; CritDamageBonus adds on top. Used when IsCritical.
    public bool IsCritical;
    public double CritDamageBonus;   // e.g. 0.60 = +60% (C.RATE / cards)

    private static int Floor(double v) => (int)Math.Floor(v);

    private static (int str,int agi,int vit,int intt,int dex,int luk,int atkPct,int matkFlat) Sum(IEnumerable<GearBonus> b)
    {
        int s=0,a=0,v=0,i=0,d=0,l=0,mf=0; double ap=0;
        foreach (var g in b) { s+=g.Str; a+=g.Agi; v+=g.Vit; i+=g.Int; d+=g.Dex; l+=g.Luk; ap+=g.AtkPercent; mf+=g.FlatMatk; }
        return (s,a,v,i,d,l,(int)Math.Round(ap*100),mf);
    }

    public DamageResult Calculate(StatBlock st, Weapon w, Target t, IReadOnlyList<GearBonus> gear, SkillProfile skill)
    {
        var sum = Sum(gear);
        int str = st.Str + sum.str, dex = st.Dex + sum.dex, luk = st.Luk + sum.luk, intt = st.Int + sum.intt;
        bool ranged = w.Class == WeaponClass.Ranged;
        var atkElement = skill.ForcedElement ?? w.Element;

        double sb = gear.Sum(g => g.SkillPercent);
        double racePct = gear.Where(g => g.RaceTarget == null || g.RaceTarget == t.Race).Sum(g => g.RacePercent);
        double sizePct = gear.Where(g => g.SizeTarget == null || g.SizeTarget == t.Size).Sum(g => g.SizePercent);
        double elemPct = gear.Where(g => g.ElementTarget == null || g.ElementTarget == t.Element).Sum(g => g.ElementPercent);
        double atkPct  = gear.Sum(g => g.AtkPercent);
        double skillMult = (Mode == CalcMode.Renewal ? skill.RenewalMultiplier : skill.ClassicMultiplier);
        double elemMod = Elements.Modifier(atkElement, t.Element, t.ElementLevel);

        double baseMin, baseMax;
        if (skill.Magic) { (baseMin, baseMax) = Matk(st, w, intt, dex, luk, sum.matkFlat); }
        else             { (baseMin, baseMax) = Atk(st, w, str, dex, luk, ranged, sum.atkPct); }

        double critMult = (!skill.Magic && IsCritical) ? (1.40 + CritDamageBonus) : 1.0;

        double Pipe(double atk)
        {
            double d = atk * skillMult * skill.Hits;
            d *= (1 + racePct) * (1 + sizePct) * (1 + elemPct) * (1 + sb);  // card/enchant %s
            d *= elemMod;                                                   // element table
            d *= critMult;                                                  // crit (physical)
            d = skill.Magic ? ReduceMdef(d, t) : ReduceDef(d, t);           // defense
            return Math.Max(0, d);
        }

        var r = new DamageResult { Min = Floor(Pipe(baseMin)), Max = Floor(Pipe(baseMax)) };
        r.Avg = Floor((r.Min + r.Max) / 2.0);
        r.Breakdown = $"{Mode} {(skill.Magic ? "MATK" : "ATK")} base {Floor(baseMin)}–{Floor(baseMax)}, " +
                      $"skill ×{skillMult:0.##}×{skill.Hits}, elem {elemMod:0.##}, " +
                      $"race+{racePct:P0} size+{sizePct:P0} elem%+{elemPct:P0}{(critMult>1?$", crit ×{critMult:0.##}":"")}";
        return r;
    }

    // ---- ATK (physical) ----
    private (double,double) Atk(StatBlock st, Weapon w, int str, int dex, int luk, bool ranged, int atkPctFromCards)
    {
        if (Mode == CalcMode.Classic)
        {
            // Pre-renewal BaseATK
            double primary = ranged ? dex : str;
            double secondary = ranged ? str : dex;
            double baseAtk = primary + Floor(primary / 10.0) * Floor(primary / 10.0) / 1.0 // (primary/10)^2
                           + Floor(secondary / 5.0) + Floor(luk / 5.0);
            double weapon = w.BaseDamage + RefineAtk(w.Level, w.Refine);
            double variance = 0.05 * w.Level * w.BaseDamage;
            double lo = baseAtk + (weapon - variance), hi = baseAtk + (weapon + variance);
            double m = 1 + atkPctFromCards / 100.0;
            return (lo * m, hi * m);
        }
        else
        {
            // Renewal / Fourth StatusATK — exact rAthena status_base_atk (4CrAM-EX src/map/status.cpp):
            //   PC: (dstr*10 + dex*10/5 + luk*10/3 + level*10/4)/10 + 5*POW
            //   bow/gun swap STR<->DEX (dstr=dex). POW term only exists for 4th-job stats.
            int pstr = ranged ? dex : str;   // dstr
            int pdex = ranged ? str : dex;
            int statusAtk = Floor((pstr * 10 + pdex * 10 / 5 + luk * 10 / 3 + st.BaseLevel * 10 / 4) / 10.0);
            if (Mode == CalcMode.Fourth) statusAtk += st.Pow * 5;   // POW: +5 StatusATK / point
            double statBonus = w.BaseDamage * (ranged ? dex : str) / 200.0;
            double refine = RefineAtk(w.Level, w.Refine);       // exact rAthena refine.yml (Bonus/100)
            double variance = 0.05 * w.Level * w.BaseDamage;
            double weaponLo = (w.BaseDamage - variance) + statBonus + refine;
            double weaponHi = (w.BaseDamage + variance) + statBonus + refine;
            double groupA = 1 + atkPctFromCards / 100.0;
            double lo = statusAtk * 2 + weaponLo * groupA;
            double hi = statusAtk * 2 + weaponHi * groupA;
            if (Mode == CalcMode.Fourth)
            {
                // level-5 weapons grant +2 P.Atk per refine (rAthena status.cpp, RENEWAL).
                double pAtk = st.Pow / 3.0 + st.Con / 5.0 + (w.Level == 5 ? w.Refine * 2 : 0);
                lo *= 1 + pAtk / 100.0; hi *= 1 + pAtk / 100.0;
            }
            return (lo, hi);
        }
    }

    // ---- MATK ----
    private (double,double) Matk(StatBlock st, Weapon w, int intt, int dex, int luk, int flatMatk)
    {
        if (Mode == CalcMode.Classic)
        {
            double min = intt + Floor(intt / 7.0) * Floor(intt / 7.0);
            double max = intt + Floor(intt / 5.0) * Floor(intt / 5.0);
            return (min + flatMatk, max + flatMatk);
        }
        else
        {
            int statusMatk = Floor(intt * 1.5) + Floor(dex / 5.0) + Floor(luk / 3.0);
            if (Mode == CalcMode.Fourth) statusMatk += st.Spl * 5;   // SPL: +5 status magic atk / point
            double variance = 0.05 * w.Level * w.FlatMatk;
            double lo = statusMatk + (w.FlatMatk - variance) + flatMatk;
            double hi = statusMatk + (w.FlatMatk + variance) + flatMatk;
            if (Mode == CalcMode.Fourth)
            {
                double sMatk = st.Spl / 3.0 + st.Con / 5.0;          // S.MATK: final +% multiplier
                lo *= 1 + sMatk / 100.0; hi *= 1 + sMatk / 100.0;
            }
            return (lo, hi);
        }
    }

    // Cumulative weapon ATK from refine per weapon level (rAthena db/re/refine.yml, Bonus/100), +1..+20.
    private static readonly int[][] WeaponRefineAtk =
    {
        new[]{2,4,6,8,10,12,14,16,18,20,22,24,26,28,30,48,51,54,57,60},        // Lv1
        new[]{3,6,9,12,15,18,21,24,27,30,33,36,39,42,45,80,85,90,95,100},      // Lv2
        new[]{5,10,15,20,25,30,35,40,45,50,55,60,65,70,75,112,119,126,133,140},// Lv3
        new[]{7,14,21,28,35,42,49,56,63,70,77,84,91,98,105,160,170,180,190,200},// Lv4
        new[]{8,16,24,32,40,48,56,64,72,80,88,96,104,112,120,128,136,144,152,160},// Lv5
    };
    private static int RefineAtk(int weaponLevel, int refine)
    {
        if (refine <= 0 || weaponLevel < 1) return 0;
        var arr = WeaponRefineAtk[(weaponLevel > 5 ? 5 : weaponLevel) - 1];
        return arr[(refine > arr.Length ? arr.Length : refine) - 1];
    }

    private static double ReduceDef(double dmg, Target t)
    {
        // Renewal hard DEF (rAthena): dmg * (4000 + hardDef) / (4000 + 10*hardDef), then - softDef.
        double afterHard = dmg * (4000.0 + t.HardDef) / (4000.0 + 10.0 * t.HardDef);
        return afterHard - t.SoftDef;
    }
    private static double ReduceMdef(double dmg, Target t)
    {
        double afterHard = dmg * (4000.0 + t.HardMdef) / (4000.0 + 10.0 * t.HardMdef);
        return afterHard - t.SoftMdef;
    }

    /// <summary>Sensitivity advisor: bump each lever a little, measure damage gain, and rank them so
    /// the user knows what to focus on next (e.g. crit-damage-heavy skills like Sharpshooting).</summary>
    public string Advise(StatBlock st, Weapon w, Target t, IReadOnlyList<GearBonus> gear, SkillProfile skill)
    {
        double Base() => Calculate(st, w, t, gear, skill).Avg;
        double b = Base();
        if (b <= 0) return "Set stats/weapon first.";

        var gains = new List<(string lever, double pct)>();
        void Try(string lever, Action apply, Action undo)
        { apply(); double g = (Calculate(st, w, t, gear, skill).Avg - b) / b; undo(); if (g > 0.0001) gains.Add((lever, g)); }

        if (!skill.Magic)
        {
            Try("+10 STR", () => st.Str += 10, () => st.Str -= 10);
            Try("+10 DEX", () => st.Dex += 10, () => st.Dex -= 10);
            Try("+1 refine", () => w.Refine += 1, () => w.Refine -= 1);
            if (IsCritical || skill.Name.Contains("Sharp", StringComparison.OrdinalIgnoreCase))
            {
                bool wasCrit = IsCritical; IsCritical = true;
                Try("+20% crit dmg", () => CritDamageBonus += 0.20, () => CritDamageBonus -= 0.20);
                IsCritical = wasCrit;
            }
        }
        else
        {
            Try("+10 INT", () => st.Int += 10, () => st.Int -= 10);
            Try("+10 DEX", () => st.Dex += 10, () => st.Dex -= 10);
            Try("+1 weapon MATK", () => w.FlatMatk += 1, () => w.FlatMatk -= 1);
        }

        if (gains.Count == 0) return "Diminishing returns on stats — improve gear/cards or change element.";
        var top = gains.OrderByDescending(x => x.pct).ToList();
        var sb = new StringBuilder();
        sb.Append("Focus next: ").Append(top[0].lever).Append($" (+{top[0].pct:P1} dmg)");
        if (top.Count > 1) sb.Append("; then ").Append(top[1].lever).Append($" (+{top[1].pct:P1})");
        return sb.ToString();
    }
}
