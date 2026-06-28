# Tool Architecture — how rAthena + GRF knowledge powers 4ViviTools

> **Re-extract:** the whole pipeline is `tools/extract/*.py` → `src/4rVivi.Core/Data/gamedata.json`. See [RE-EXTRACT.md](RE-EXTRACT.md).

This ties every knowledge doc to the code that uses it. Three knowledge sources feed one data file,
which feeds every feature.

## Data flow
```
rAthena server (db/re YAML + src/map C++)  ─┐
GRF client (System/ + luafiles514 lua)     ─┼─►  tools/extract/*.py  ─►  gamedata.json (embedded)
                                            ─┘        gen2 / combos / skills_gen / ratios
                                                              │
        ┌─────────────────────────────────────────────────────┼─────────────────────────────────┐
   Calculator                         Trackers (MVP/DB)        4rTools / ro-tools            OCR / LiveStats
```

## gamedata.json schema (current)
| Array | Fields | Backed by doc |
|-------|--------|---------------|
| `equips` | id, name, type, subtype, loc[], **jobs[]**, wlvl, atk, matk, def, slots, **mods[]** | item-bonuses, weapon-equip-per-class |
| `cards` | id, name, loc[], mods[] | item-bonuses |
| `enchants` | id, name, mods[] | client-systems |
| `combos` | sets[][], mods[] | item-combos |
| `mobs` | id, name, level, hp, atk, def, mdef, str…luk, race, element(+lv), size, mvp, drops | monsters, elements |
| `skills` | id, name, aegis, hits, **mult**, element, type, magic, atk | skills |
| `skillCatalog` | class → [offensive skill names] | classes, skills, client-grf-data |
| `items` | id, aegis, name, type, slots, weight | (search/buffs) |

`ModEntry` = parsed item script bonus (str…luk, atk, matk, atkp, race+racep, size+sizep, ele+elep).

## Feature ↔ knowledge map

### Calculator (`CalculatorViewModel`, `DamageCalculator`)
- **Damage**: [damage-formula](damage-formula.md), [stats-substats](stats-substats.md),
  [elements](elements.md), [refine](refine.md), [size-modifier](size-modifier.md).
- **Gear effect**: [item-bonuses](item-bonuses.md) → `mods` → `GearBonus`; [item-combos](item-combos.md).
- **Weapon**: [weapon-types](weapon-types.md) (ranged/melee), [weapon-equip-per-class](weapon-equip-per-class.md)
  (`ClassEquip` filters the weapon picker to the class's `jobs`).
- **Skills**: [skills](skills.md) → auto multiplier/hits/element; class filter via `skillCatalog`.
- **Enemy**: [monsters](monsters.md) auto-fill; [classes](classes.md) for mode/group filter.

### Trackers
- **MVP tracker**: [monsters](monsters.md) (respawn, MVP flag) + [elements](elements.md) (best element vs MVP).
- **Database view**: all arrays + GRF icons ([client-grf-data](client-grf-data.md)).

### 4rTools / ro-tools (OCR-driven shells)
- **Skill Spammer** (`ClassSkillsViewModel`): class → skills with GRF skill icons
  ([classes](classes.md), [client-grf-data](client-grf-data.md)); can also consume `skillCatalog`.
- **Autopot / Buffs / Debuff / Skill timers**: keyed automation; the **status-effect** model
  ([status-effects](status-effects.md), [client-systems](client-systems.md) EFST icons) is the basis
  for buff/debuff detection on the HUD.
- **ATK×DEF / Macros / Songs**: macro engine; weapon/skill metadata informs which keys to send.
- All read **HP/SP/name from OCR (LiveStats)**, not memory.

### OCR (`OcrService`, `LiveStats`)
- The client name tables (`idnum2itemdisplaynametable.txt`, `skillnametable.txt`, `msgstringtable.txt`)
  are the **ground-truth dictionary** to snap fuzzy OCR reads to real item/skill/map names
  ([client-grf-data](client-grf-data.md)). HP/SP/level reads feed every automation tool.

## Where the code lives
- Data models: `src/4rVivi.Core/Data/Models.cs`; load/query: `GameDatabase.cs`.
- Formulas: `src/4rVivi.Core/Calc/` (`DamageCalculator`, `Elements`, `ClassCatalog`, `ClassEquip`).
- Calculator UI/VM: `src/4rVivi.App/Views/CalculatorView.axaml`, `ViewModels/CalculatorViewModel.cs`.
- Shells: `FourRToolsShellView` / `RoToolsShellView` (OCR header from `LiveStats`).
- Icons: `Services/IconImageService.cs` (GRF/divine-pride, by id and by name).
- Regeneration: `tools/extract/` + `tools/build_gamedata.py`.
