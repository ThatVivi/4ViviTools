# Item Combos ("group of gears")

> **Re-extract:** `tools/extract/combos.py`; raw: `sed -n '/Body:/,/Combo:/p' $RE/item_combos.yml`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/item_combos.yml`, resolved in `src/map/pc.cpp` / `itemdb.cpp`.

## Concept
When **all** items of a combo set are equipped at once, an extra script runs. This is how "wearing
the full set" or "weapon + matching shield" grants additional bonuses.

## Structure
```yaml
- Combos:
    - Combo: [Dragon_Slayer, Dragon_Breath]   # AEGIS names, min 2 items
    - Combo: [Gae_Bolg, Dragon_Breath]        # alternative sets sharing the same Script
  Script: bonus2 bAddRace,RC_Dragon,5;
```
One entry can list several alternative `Combo` sets that all grant the same `Script`.

## Notes
- Items referenced by **AEGIS name**; the tool resolves them to display names at build time.
- A combo can mix weapon + card + armor.
- Cards inside combos count when slotted in the right gear.

## Tool mapping
`build_gamedata.py` emits a `combos` array: `{ sets: [[name,…],…], mods: [...] }` (only combos whose
script parsed to damage mods are kept — 1,753 of them). `CalculatorViewModel.BuildGear()` collects
every worn item name, then for each combo, if any one set is fully worn, adds its `mods`.
