# Original ro-tools (uniaodk, reference) ↔ our replica

> **Re-extract:** in `all/ro-tools-main/ro-tools-main/`: `ls events/ game/ service/ gui/widget/` ; read `game/map_buffs.py`, `events/auto_pot_hp.py`, `service/memory.py`.

Source: `ro-tools-main` — **Python + PyQt** app. More advanced than 4RTools (includes a real bot with
pathfinding). We replicate its feature set, feeding **OCR** instead of memory where possible.

## How it reads the game (the part we replace)
- **`service/memory.py` + `service/offsets.py` + `servers.json`** — reads the RO client's memory via
  per-server offsets (HP/SP, map, position, status array).
- **`game/map_buffs.py`** — `DIC_SKILL_BUFF` / `DIC_ITEM_BUFF` / `DIC_STATUS_DEBUFF`: map a **status id
  → effect name** (e.g. `"1": "sm_endure"`, `"2": "kn_twohandquicken"`, `"3": "ac_concentration"`).
  These are the in-game status/EFST ids = rAthena skill/status ids (see [status-effects.md](status-effects.md),
  [client-systems.md](client-systems.md)). This table is how it knows which buffs are active.
- **`service/keyboard.py` / `service/mouse.py`** — input send; **`service/map_gat.py`** — map cell
  (walkability) data for the bot.

## Modules
**`game/`** (state model): `player.py`, `char.py`, `world.py`, `buff.py`, `jobs.py`, `map_buffs.py`,
`macro.py`, `spawn_skill.py`, `entity.py`.

**`events/`** (features, each a loop):
| Event | Feature |
|-------|---------|
| `auto_pot_hp` / `auto_pot_sp` | HP/SP auto-potion (key + %, optional per-map/delay) |
| `auto_item_buff` / `auto_item_debuff` | use buff items / cure debuffs |
| `skill_buff` / `skill_equip` | recast skill buffs / swap equipment buffs |
| `skill_spawmmer` | skill spammer |
| `auto_element` | keep weapon element (endow/converter) |
| `auto_teleport` / `auto_ygg` / `auto_abracadabra` / `auto_halter_lead` | utility automations |
| `auto_commands` / `hotkey_event` / `macro_event` | chat commands, hotkeys, macros |
| `bot_event` / `game_event` | the bot loop + game tick |

**`service/bot_*`** (the bot): `bot_combat_actions`, `bot_combat_rules`, `bot_pathfinder`,
`bot_patrol`, `bot_patrol_router`, `bot_walk`, `bot_screen_coords`, `pathfinding`, `map_gat`.

**`gui/`** (PyQt): `painel_auto_element`, `painel_auto_item_buff/debuff/hp_sp`, `painel_bot`,
`cbox_jobs/item/skill/macro/process`, and many `input_*` widgets (keybind, delay, timer, mvp, teleport…).

**`config.json`**: e.g. `auto_item.hp_potion = { key:"F9", percent:60, delay_active, map_active }`,
`sp_potion`, `ygg`, etc.

## Mapping to our replica (`RoToolsShellView` / `RoToolsShellViewModel`)
| ro-tools | Ours |
|----------|------|
| memory + offsets (HP/SP/status) | **OCR `LiveStats`** |
| DIC_SKILL/ITEM/STATUS dicts | EFST/status model (planned: status-bar OCR) |
| auto_pot_hp/sp | Auto HP/SP tab |
| auto_item_buff / skill_buff | Item Buff / Skill Buff tabs |
| skill_spawmmer | Skill Spammer tab (class skills + icons) |
| auto_element | Auto Element tab |
| macro_event / hotkey | Macro tab |
| bot_event + bot_* | Bot / Smart Bot tabs |
| painel_bot stats | Statistics tab |

Differences: ro-tools' deep **pathfinding bot** (map_gat + A*) is beyond our current Bot tab; our replica
matches the buff/pot/skill/element/macro feature set, OCR-fed, in the dark v1.6.1 layout.
