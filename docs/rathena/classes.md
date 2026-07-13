# Classes / Jobs

> **Re-extract:** `grep -E "JT_NOVICE|JT_KNIGHT" $GRF/jobidentity.lub` ; `sed -n '/SKILL_TREEVIEW_FOR_JOB/,/JT_MAGICIAN/p' $GRF/skilltreeview.lub`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/pc.hpp` (job constants, `MAPID_*`), `db/re/job_*.yml`, `db/re/skill_tree.yml`.

## Progression
1st → 2nd → Transcendent (Rebirth) → 3rd → **4th**. Plus Expanded classes (Super Novice,
Gunslinger/Rebellion, Ninja/Kagerou/Oboro, Taekwon line, Soul Linker, Doram) and Baby variants.

## Class groups (calculator filter)
- **Normal** — standard 1st→4th lines.
- **Baby** — adopted/baby variants (lower stat caps).
- **Extended** — Super Novice, gunner, ninja, taekwon, soul, Doram families.

## Trait stats unlock
Only **4th classes** have POW/STA/WIS/SPL/CON/CRT and the derived P.Atk/S.Matk/Res/Mres/HPlus/CRate
(see [stats-substats.md](stats-substats.md)). Base level up to 250+, job to 50/55.

## Formula modes
| Mode | Formula family |
|------|----------------|
| Classic | pre-renewal BaseATK/MATK |
| Reborn (Trans) | pre-renewal + transcendent HP/SP/skill bonuses |
| Renewal (Lv175) | renewal StatusATK/MATK |
| Renewal (Lv185) | renewal (extended level cap; same equations) |
| 4th Class | renewal + trait stats (POW→StatusATK, P.Atk, etc.) |

Level cap differs (175 vs 185 vs 250) but the **damage equations** for the two renewals are the same;
the difference is the level term and stat budget.

## Per-job data
- `job_basepoints.yml` — base HP/SP per level per job.
- `job_aspd.yml` — weapon delay (ASPD) per job per weapon type.
- `job_stats.yml` — stat bonuses per job/level.
- `skill_tree.yml` — which skills each job can learn (maps class → skills).

## Tool mapping
`src/4rVivi.Core/Calc/ClassCatalog.cs` lists classes by group; the calculator's Normal/Baby/Extended
checkboxes filter the dropdown. The mode selector picks the formula family in `DamageCalculator`.
