# rAthena / Ragnarok Online — Knowledge Base

Reference notes distilled from the rAthena source (`src/map`) and renewal databases (`db/re`),
focused **only on game mechanics** relevant to 4ViviTools (calculator, trackers, OCR, automation).
Netcode, SQL, packet handling, and server administration are intentionally ignored.

Each file documents one mechanic: what it is, the exact rAthena formula/data, and how the tool uses it.

**Start here:** [tool-architecture.md](tool-architecture.md) — how all this knowledge flows into the
project (calculator, trackers, 4rTools, ro-tools, OCR), with the `gamedata.json` schema.

**Re-extraction:** every topic has a runnable command/script in [RE-EXTRACT.md](RE-EXTRACT.md), and the
full data pipeline lives in `tools/extract/` (`gen2.py`, `combos.py`, `skills_gen.py`, `ratios.py`).

## Index

| File | Topic |
|------|-------|
| [damage-formula.md](damage-formula.md) | Physical & magic damage pipeline (ATK → modifiers → DEF → result) |
| [stats-substats.md](stats-substats.md) | Primary stats, trait stats, and every derived sub-stat |
| [elements.md](elements.md) | Element modifier table (attr_fix) and best-element logic |
| [refine.md](refine.md) | Refine ATK/DEF bonus tables, over-refine, grade |
| [item-bonuses.md](item-bonuses.md) | `bonus`/`bonus2`/`bonus3` script grammar and key types |
| [item-combos.md](item-combos.md) | Multi-item set bonuses ("group of gears") |
| [weapon-types.md](weapon-types.md) | Weapon type enum, dual-wield, ranged vs melee |
| [weapon-equip-per-class.md](weapon-equip-per-class.md) | `pc_equippoint`/`pc_isequip`: which class can equip what |
| [monsters.md](monsters.md) | mob_db fields, MVP flag, element/race/size/def |
| [classes.md](classes.md) | Job tree, 1st→4th classes, groups, trait-stat unlock |
| [skills.md](skills.md) | skill_db metadata, ratio location, element source, class tree |
| [aspd.md](aspd.md) | Attack speed: BaseASPD, AGI/DEX, bonuses |
| [size-modifier.md](size-modifier.md) | Weapon size penalty (renewal vs classic) |
| [status-effects.md](status-effects.md) | Buffs & debuffs (SC_*), element interactions |
| [hp-sp.md](hp-sp.md) | HP/SP/AP base formulas, VIT/INT scaling |
| [mounts.md](mounts.md) | Peco/Dragon/Warg/Mado option flags |
| [ammo-arrows.md](ammo-arrows.md) | Arrows/bullets/kunai: ATK, element override |
| [maps-woe-pvp.md](maps-woe-pvp.md) | Mapflags, WoE/PvP/PvM combat differences |
| [level-penalty.md](level-penalty.md) | EXP/drop modifier by level gap |
| [client-grf-data.md](client-grf-data.md) | Client `System/` & lua files ↔ rAthena (names, sprites, skill trees, OCR) |
| [client-systems.md](client-systems.md) | Enchant, reform, status icons/HUD, skill effects, options ↔ rAthena |
| [icons.md](icons.md) | Baking GRF item+skill sprites into the offline icon pack |
| [ocr.md](ocr.md) | OCR pipeline, dictionary correction, retraining steps |
| [ocr-data-refresh.md](ocr-data-refresh.md) | Refresh OCR vocabularies from live rAthena databases |
| [db-files.md](db-files.md) | What every `db/re` file holds and its relevance |
| [original-4rtools.md](original-4rtools.md) | The original 4RTools (C# WinForms) ↔ our replica |
| [4rtools-internals.md](4rtools-internals.md) | 4RTools file-by-file: memory layout, EFST, every model |
| [original-ro-tools.md](original-ro-tools.md) | The original ro-tools (Python/PyQt) ↔ our replica |
| [ro-tools-bot-and-states.md](ro-tools-bot-and-states.md) | Character state codes (sit/attack/idle) + combat-bot config |
| [faithful-replica-blueprint.md](faithful-replica-blueprint.md) | Exact per-tab controls to rebuild both tools 1:1 |
| [4rtools-ui-spec.md](4rtools-ui-spec.md) | 4RTools v2.10 — full control-by-control UI spec |
| [ro-tools-ui-spec.md](ro-tools-ui-spec.md) | RO-Tools v1.8 — full control-by-control UI spec |
| [tool-architecture.md](tool-architecture.md) | How all knowledge flows into the project |

## Source layout (mechanics-relevant only)

- `src/map/status.cpp` — stat & sub-stat derivation (`status_calc_*`, `status_base_atk`).
- `src/map/battle.cpp` — the damage calculation pipeline (`battle_calc_*`).
- `src/map/pc.cpp` — player: equip, weapon type (`pc_calcweapontype`), job logic.
- `src/map/skill.cpp` — per-skill damage ratios, hits, element overrides.
- `db/re/*.yml` — items, mobs, skills, refine, elements, jobs, enchants, combos.

## How the tool consumes this

`tools/build_gamedata.py` parses the `db/re` YAMLs into `src/4rVivi.Core/Data/gamedata.json`
(equips per slot, cards, enchants, combos, mobs with full stats, item scripts → structured `mods`).
`src/4rVivi.Core/Calc/DamageCalculator.cs` implements the formulas documented here.
