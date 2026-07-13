# ASPD (Attack Speed)

> **Re-extract:** `grep -nE "BaseASPD" $RE/job_basepoints.yml` ; `sed -n '/int16 status_calc_aspd\(/,/return/p' $SRC/status.cpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/job_basepoints.yml` (`BaseASPD`), `src/map/status.cpp` (`status_calc_aspd`, `status_calc_aspd_rate`).

## Concept
Displayed **ASPD = 200 − amotion/something**; higher ASPD = faster attacks (cap 193–199 depending on
server). Each job has a **BaseASPD per weapon type** (default 2000 = slowest), reduced by AGI and DEX
plus ASPD bonuses.

## Inputs
- **BaseASPD[weaponType]** per job (`job_basepoints.yml`). Two-hand/heavy weapons start slower.
- **AGI** — the main reducer: `delay -= f(AGI)` (roughly AGI×DEX weighted, AGI dominant).
- **DEX** — secondary reducer.
- **AspdRate / bAspd / bAspdRate** bonuses from gear, foods, skills (e.g. Two-Hand Quicken, Berserk,
  Awakening/Concentration potions).

## Formula gist (Renewal)
`amotion = baseAspd_weapon × (1 − (4×AGI + DEX) / 1000) ... ` then apply flat `bAspd` and `%bAspdRate`.
Final ASPD ≈ `200 − amotion/10`. Exact constants are in `status_calc_aspd` (status.cpp ~8237).

## Why it matters
DPS = single-hit damage × attacks/second; ASPD sets the rate. The calculator's DPS uses the damage
model; full ASPD modelling (job BaseASPD + AGI/DEX + buffs) is a planned refinement.

## Tool mapping
ASPD is currently surfaced from the stat calculator in the character readout; the precise
weapon/job ASPD curve is a ◻ item in [db-files.md](db-files.md).
