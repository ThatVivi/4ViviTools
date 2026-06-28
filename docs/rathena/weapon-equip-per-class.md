# Weapon / Equip Restrictions per Class

> **Re-extract:** `sed -n '/int32 pc_equippoint(/,/^}/p' $SRC/pc.cpp` and `sed -n '1850,1960p' $SRC/pc.cpp` (pc_isequip). See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/pc.cpp` (`pc_equippoint`, `pc_equippoint_sub`, `pc_isequip`), item `Jobs:`/`Locations:` in `item_db_equip.yml`.

## Two different questions
1. **Where does an item go?** → `pc_equippoint(sd, n)` → `pc_equippoint_sub()`. Returns the equip
   position (from the item's `Locations`), and upgrades a 1H Dagger/Sword/Axe to **both arms** if the
   wearer can dual-wield (see [weapon-types.md](weapon-types.md)).
2. **May this class equip it?** → `pc_isequip(sd, n)`. The gatekeeper.

## `pc_isequip` restriction chain (in order)
1. **GM override** — `PC_PERM_USE_ALL_EQUIPMENT` bypasses all checks.
2. **Level "look" allowances** — base level **90+** can equip *all helms*; base level **96+** can equip
   level-4 weapons of certain subtypes regardless of job (the "look" rule).
3. **Equip level** — `item->elv` (min) and `item->elvmax` (max) vs base level.
4. **Gender** — `item->sex` (male/female/both).
5. **Job mask** — `item->class_` = the item's **`Jobs:` bitmask**; the player's job must be in it.
   **This is the core "which class can wear/wield it" rule.**
6. **Ammo match** — ammo `subtype` must match the equipped weapon type (Arrow↔Bow, Bullet↔gun,
   Kunai↔Kagerou/Oboro, Cannonball↔Genetic w/ specifics, Mado shells, etc.).
7. **Strip status** — `SC_STRIPWEAPON`/`SC_STRIPSHIELD` block equipping.

## How "class → wieldable weapons" is encoded
- Each equip lists allowed **`Jobs:`** in `item_db_equip.yml` (the bitmask). A weapon usable by Knights
  lists `Knight: true` (and any others).
- **`job_aspd.yml` `BaseASPD`** also tells you a job's wieldable weapons indirectly: a job has an ASPD
  entry per weapon type it can use.
- **Weapon level** + **EquipLevelMin** further gate access (e.g. lv4 weapons need higher level).

## Worked example
"What can a Knight equip?" = all `item_db_equip` entries whose `Jobs` includes Knight, filtered by the
slot, weapon level, and the Knight's level. Weapons: typically Sword/2H Sword/Spear/2H Spear (per the
Jobs masks). Daggers can be off-handed only with the dual-wield rule.

## Tool mapping
Our generator can keep each equip's `jobs` list (from `Locations`/`Jobs`) so the calculator can filter
gear pickers by the selected class (e.g. only show weapons a Knight can wield). Currently the data
carries slot + subtype + ATK; re-adding `jobs` enables per-class equip filtering — a clean next step.
