# Refinement

> **Re-extract:** parse `$RE/refine.yml` Weapon group (`Bonus/100`) + `grep "wa->atk2 += info->bonus / 100" $SRC/status.cpp`. Script in [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/refine.yml`, applied in `src/map/status.cpp` (`status_calc_pc_`).

## How refine bonus is applied
For a weapon: `weaponATK2 += refine.yml Bonus / 100`. The stored `Bonus` is the **cumulative total**
at that refine level (×100 for precision). So the per-level ATK = `Bonus/100`.

## Weapon ATK by refine (Bonus/100), +1 … +20

| WLv | +1..+15 (safe, linear) | +16 | +17 | +18 | +19 | +20 |
|-----|------------------------|-----|-----|-----|-----|-----|
| 1 | +2 per level (→30) | 48 | 51 | 54 | 57 | 60 |
| 2 | +3 per level (→45) | 80 | 85 | 90 | 95 | 100 |
| 3 | +5 per level (→75) | 112 | 119 | 126 | 133 | 140 |
| 4 | +7 per level (→105) | 160 | 170 | 180 | 190 | 200 |
| 5 | +8 per level (→120) | 128 | 136 | 144 | 152 | 160 |

The jump at **+16** is the over-refine bonus (safe limit is usually +15 for HD/normal).

## Over-refine random bonus
`overrefine = randombonus_max / 100` — extra **random** ATK (0..max) added to the **max** of the
damage range on high refines.

## Level-5 weapon trait bonus (Renewal)
A level-5 weapon also grants **+2 P.Atk and +2 S.Matk per refine level**.

## Armor / Shadow gear
Armor refine adds soft DEF (`refinedef`), Shadow gear has its own group. Not relevant to outgoing
damage, so the calculator currently models weapon refine only.

## Tool mapping
`DamageCalculator.WeaponRefineAtk[][]` is the exact table above; `RefineAtk(wlv, refine)` looks it up.
Level-5 P.Atk per refine is added in the 4th-class branch.
