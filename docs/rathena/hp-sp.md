# HP / SP / AP

> **Re-extract:** `grep -nE "HpFactor|HpIncrease|SpFactor|SpIncrease|MaxStats" $RE/job_basepoints.yml`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/job_basepoints.yml`, `src/map/status.cpp` (`status_calc_maxhp/maxsp/maxap`).

## Base HP/SP (when HP_SP_TABLES disabled)
Per job, per base level:
- **HP** = `HpIncrease/100` (linear) `+ HpFactor × BaseLv / 100` (exponential).
- **SP** = `SpIncrease/100 + SpFactor × BaseLv / 100`.
- **AP** (4th classes) = `ApIncrease/100 + ApFactor × BaseLv / 100`.

Modern servers usually use precomputed **HP/SP tables** instead, but the factors are the fallback.

## Stat multipliers
- **MaxHP × (1 + VIT/100)** roughly (VIT raises HP and adds soft DEF + status resist).
- **MaxSP × (1 + INT/100)** roughly (INT raises SP and MATK + soft MDEF).
- Gear `bMaxHP`, `bMaxHPrate`, `bMaxSP`, `bMaxSPrate` add flat/percent.

## Job data
`job_basepoints.yml` also carries `MaxWeight`, `BaseASPD`, `BonusStats` (per job level stat gains),
and `MaxStats` (stat caps incl. trait stats for 4th).

## Tool mapping
The character readout shows Max HP/SP from the stat calculator. Exact per-job HP/SP tables are a ◻
refinement (see [db-files.md](db-files.md)); not needed for outgoing-damage accuracy.
