# Stats & Sub-stats

> **Re-extract:** `grep -nE "status->patk|status->smatk|status->hit|status->flee|status->def2|status->res|status->mres|status->hplus|status->crate" $SRC/status.cpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/status.cpp` (`status_calc_bl_main`, `status_base_atk`). Renewal formulas.

## Primary stats
STR, AGI, VIT, INT, DEX, LUK — capped per job. Final value = base + job bonus + equipment + buffs.

## Trait stats (4th classes only)
POW, STA, WIS, SPL, CON, CRT. Each feeds a derived sub-stat below.

## Derived sub-stats (Renewal, players)

| Sub-stat | Formula |
|----------|---------|
| **StatusATK** | `(dstr·10 + dex·10/5 + luk·10/3 + level·10/4)/10 + 5·POW` — for bow/gun, dstr=DEX (STR↔DEX swap) |
| **HIT** | `level + DEX + LUK/3 + 175 + 2·CON` |
| **FLEE** | `level + AGI + LUK/5 + 100 + 2·CON` |
| **DEF2 (soft def)** | `(level + VIT)/2 + AGI/5` |
| **MDEF2 (soft mdef)** | `INT + level/4 + (DEX+VIT)/5` |
| **CRIT** | `10 + LUK·10/3` (i.e. +0.3 crit per LUK) ÷10 displayed |
| **Perfect Dodge** | `LUK/10 + 10` (every 10 LUK = +1) |
| **P.Atk** | `POW/3 + CON/5` |
| **S.Matk** | `SPL/3 + CON/5` |
| **Res** (status resist) | `STA + STA/3·5` |
| **Mres** | `WIS + WIS/3·5` |
| **HPlus** (healing power) | `CRT` |
| **CRate** (crit bonus) | `CRT/3` |

## Pre-renewal (Classic) deltas
- HIT = `level + DEX`; FLEE = `level + AGI`; DEF2 = `VIT`; MDEF2 = `INT + VIT/2`.
- BaseATK = `STR + (STR/10)² + DEX/5 + LUK/5` (bow swaps STR↔DEX).

## How P.Atk / S.Matk apply
They are **final % multipliers** on physical / magic damage respectively
(`damage × (1 + P.Atk/100)`). Level-5 weapons also grant **+2 P.Atk and +2 S.Matk per refine**.

## Tool mapping
`DamageCalculator.cs` → `Atk()` / `Matk()` implement StatusATK and the P.Atk/S.Matk multipliers.
The character readout panel surfaces HP/SP/ASPD/HIT/FLEE/CRIT/DEF/MDEF.
