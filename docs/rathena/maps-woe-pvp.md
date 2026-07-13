# Maps, Mapflags, WoE / PvP / PvM

> **Re-extract:** `grep -cE "MF_[A-Z]" $SRC/map.hpp` ; `grep -nE "mf_pvp|mf_gvg|mf_restricted" $SRC/*.cpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `src/map/map.hpp` (`MF_*`, 94 flags), `src/map/atcommand.cpp`/`npc.cpp` (mapflag set), `db/re/item_noequip.txt`.

## Combat environments
- **PvM** — players vs monsters (normal fields/dungeons).
- **PvP** — player vs player; enabled by `mf_pvp`. Damage reductions differ (skill damage often
  reduced vs players via `battle_config` and `bonus2 bSubRace,RC_Player_*`).
- **WoE (War of Emperium)** — guild siege on castle maps with `mf_gvg`/`mf_gvg_castle`. Heavy damage
  reductions, no normal resurrection, emperium mechanics, skill restrictions.

## Key mapflags (of 94)
`mf_pvp`, `mf_pvp_noparty`, `mf_gvg`, `mf_gvg_castle`, `mf_gvg_dungeon`, `mf_nopvp`, `mf_noreturn`,
`mf_nowarp`, `mf_nomemo`, `mf_noteleport`, `mf_nosave`, `mf_noskill`, `mf_noicewall`, `mf_restricted`
(jail/instance), `mf_nobranch`, `mf_partylock`, `mf_battleground`, `mf_town`, `mf_nopenalty`,
`mf_restricted` (item_noequip), `mf_loadevent`, `mf_pvp_nightmaredrop`.

## Damage differences (why they matter for a calculator)
- Skill damage vs **players** is commonly scaled down (e.g. 60–70%) by `battle_config`.
- WoE/BG apply their own reduction multipliers and disable some effects.
- `item_noequip.txt` lists items disabled per environment (some gears can't be worn in WoE/PvP).

## Tool mapping
The calculator has an **Environment** selector (PvM/PvP/WoE) in the combat-sim section. The actual
PvP/WoE damage reduction multipliers are a ◻ refinement (config-driven, server-specific).
