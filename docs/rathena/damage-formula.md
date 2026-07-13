# Damage Formula

> **Re-extract:** `sed -n '/battle_calc_attack_skill_ratio/,/^}/p' $SRC/battle.cpp` ; `grep -n "status_base_atk" $SRC/status.cpp`. Full list in [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/battle.cpp` (`battle_calc_weapon_attack`, `battle_calc_attack`), `status.cpp`.

## Physical damage pipeline (Renewal)

1. **ATK = StatusATK + WeaponATK + ExtraATK**
   - StatusATK — see [stats-substats.md](stats-substats.md). Counted **×2** in the final equation.
   - WeaponATK = `weapon base ATK + statBonus + refine bonus ± variance`
     - statBonus ≈ `weaponATK × (STR or DEX for ranged) / 200`
     - refine bonus = `refine.yml Bonus / 100` (see [refine.md](refine.md))
     - variance = `±0.05 × weaponLevel × weaponATK`
   - ExtraATK = flat ATK from cards/gear (`bonus bAtk`/`bBaseAtk`).
2. **× Group-A modifiers** — ATK% (`bAtkRate`, EDP, etc.).
3. **× skill ratio × hits** — per-skill multiplier (e.g. Bash 100%+), hit count.
4. **× target modifiers** (Group B, multiplicative): vs Race%, vs Size%, vs Element%, skill%.
5. **× element table** — attacker element vs defender element/level (see [elements.md](elements.md)).
6. **× critical** — if crit: `1.4 + CritDmgBonus` (renewal base crit = 140%).
7. **− DEF**:
   - Hard DEF (renewal): `damage × (4000 + hardDef) / (4000 + 10·hardDef)`
   - then `− soft DEF (DEF2)`.
8. **P.Atk final multiplier** (4th): `× (1 + P.Atk/100)`.

## Magic damage
MATK = StatusMATK (`INT·1.5 + DEX/5 + LUK/3`, +5·SPL for 4th) + WeaponMATK + flat MATK.
Reduced by MDEF the same way as DEF (hard MDEF curve then − MDEF2). S.Matk applies as final %.

## Size modifier
Weapon does %ATK vs target size by weapon type (e.g. daggers 100/75/50 vs S/M/L). Tracked as
"Weapon Size Modifier" in the calculator.

## Tool mapping
`DamageCalculator.Calculate()` runs steps 1–8. Gear/card/combo/enchant bonuses arrive as a
`GearBonus` list (parsed from item scripts); race/size/element % apply only vs the matching target.
