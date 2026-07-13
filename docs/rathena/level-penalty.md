# Level Penalty (EXP / Drop)

> **Re-extract:** `grep -nE "Type:|Difference:|Rate:" $RE/level_penalty.yml`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/level_penalty.yml`, `src/map/pc.cpp` (`pc_gainexp`), `mob.cpp` (drops).

## What it is
A modifier on **EXP and item DROP rate** based on the **level difference** between the player and the
monster. It is **not** a damage modifier (renewal damage level-scaling is separate, in `battle_config`).

## Types
`Exp`, `Drop`, `Mvp_Exp`, `Mvp_Drop` — each has its own table of `Difference → Rate%`.

## Shape of the table
- Small gaps (within a few levels) = 100%.
- Killing monsters **higher** than you (positive difference) gives **bonus EXP/drops** up to a peak
  (~140% around +10/+11 difference), then tapers.
- Killing monsters **far below** you (negative difference) **reduces** EXP/drops (e.g. 95% at −6,
  90% at −11, less further down).

## Why it's in the knowledge base
Relevant for grinding/farming guidance and the bot/loot trackers (what's efficient to farm at a given
level), not for the damage calculator.

## Tool mapping
Not used by the calculator. Candidate data for a future "best farm spots / EXP efficiency" helper in
the tracker tools.
