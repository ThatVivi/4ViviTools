# Client Systems ↔ rAthena (Enchant, Reform, Status Icons, Effects, HUD)

> **Re-extract:** `unzip -o -j luafiles514.zip "luafiles514/lua files/{enchant,itemreform,stateicon,optioninfo}/*" -d ./out` then read; server side `$RE/{item_enchant,item_reform,status}.yml`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Companion to [client-grf-data.md](client-grf-data.md). This maps the client `System/` + `luafiles514`
gameplay systems to their **rAthena server** counterparts. The server owns logic/chances; the client
owns names, UI, icons, and effects. Linked by **ID / AegisName**.

## Lua file formats (important)
- **Plain text** (translated by the RO-English project): `skilltreeview.lub`, `skillinfolist.lub`,
  `skillid.lub`, `equipmentproperties.lub`, `itemreformsystem.lub`, `optioninfo.lub`,
  `skilleffectinfolist.lub`, `weapontable.lub`, `jobname.lub`, `npcidentity.lub`.
- **Compiled Lua 5.1 bytecode** (header `LuaQ`, not human-readable without a decompiler):
  `iteminfo.lub`, `accessoryid.lub`, `efstids.lub`, `enchantlist.lub`.
  → For these, use the **flat `.txt` fallbacks** that ship alongside:
  `idnum2itemdisplaynametable.txt`, `idnum2itemresnametable.txt`, `idnum2itemdesctable.txt`,
  `addrandomoptionnametable.lub` (text), etc.

## 1. Item Enchant System
| Server `db/re/item_enchant.yml` | Client |
|---------------------------------|--------|
| `Id`, `TargetItems`, `MinimumRefine` | `enchantlist.lub` (compiled) — enchant UI per item |
| `Slots[].Enchants[]` with `Enchantgrade`, `Item`, `Chance`, `Materials` | option names via `addrandomoptionnametable.lub` |
| `Reset` (chance/price/materials) | UI text |

Server decides **which options, at what chance, costing what**, per slot and enchant grade.

## 2. Random Options
- Server `item_randomopt_db.yml` — the option **id → Script** (the actual stat effect).
- Client `addrandomoptionnametable.lub` — option id → **display name** (e.g. "STR +1").
- Client `optioninfo/optioninfo.lub` — option UI metadata.
- The tool already parses `item_randomopt_db.yml` into the `enchants` list; names come from the client.

## 3. Item Reform — direct field match
| Server `item_reform.yml` | Client `itemreformsystem.lub` (`ReformInfo`) |
|--------------------------|----------------------------------------------|
| `Item`, `BaseItems[].BaseItem` | `BaseItem` |
| `ResultItem` | `ResultItem` |
| `ChangeRefine` / `MaximumRefine` / `MinimumRefine` | `ChangeRefineValue` / `NeedRefineMax` / `NeedRefineMin` |
| `ClearSlots`, `RemoveEnchantgrade`, `RandomOptionGroup`, `Materials` | `IsEmptySocket`, `Material`, `RandomOptionCode`, `NeedOptionNum` |

Reform = consume a base item (+ materials) to transform it into a result item, optionally changing
refine/slots/options. Same model both sides.

## 4. Equipment Properties (ammo & equip stat ranges)
`equipmentproperties.lub`: `Item[id] = { Type = "ammo", Stat = {min, max} }` — the per-item ATK (or
stat) **range** shown in tooltips. Connects to the server item's `Attack`. Useful for ammo ATK in the
calculator (see [ammo-arrows.md](ammo-arrows.md)).

## 5. Status Icons / HUD Buff Bar
The buff/debuff bar is the visible side of the server's status changes:
`SC_*` (server) → `status.yml` `Icon: EFST_x` → client `efstids.lub` (EFST id) → `stateiconinfo.lub`
(icon sprite + tooltip). Example: `Status: Bleeding → Icon: EFST_BLOODING`.
This is the data behind **HUD buff tracking** (which buffs/debuffs are active, their icons/timers).

## 6. Skill Effects
`skilleffectinfolist.lub`: `SKILL_EFFECT_INFO_LIST[SKID.x] = { beginMotionType, waveFileName (sound),
targetEffectID = {EFID.*}, onTarget }`. Maps a skill to its **animation, sound, and visual effect**.
Relevant when adding **custom skills** (server `skill_db.yml` + this client entry must agree), and for
recognising skill casts visually.

## 7. Client Options / HUD toggles
`optioninfo.lub` → `CmdOnOffOderList` = the `/command` toggles: `/notrade, /noshift, /effect, /aura,
/showname, /stateinfo, /skillsnap, /miss, /bgm, /sound, …`. These are **client HUD/UX options**
(what the player can turn on/off), relevant to the tool's HUD-options surface.

## Folder inventory (luafiles514) — relevance
| Folder | Contents | Relevance |
|--------|----------|-----------|
| skillinfoz / newskillinfo | skill DB, tree, delays, descriptions | ✅ skill catalog |
| stateicon | EFST ids, status icon info | buff/debuff HUD |
| skilleffectinfo | skill → motion/sound/effect | custom skills, cast recognition |
| enchant / optioninfo | enchant + random option UI | enchant system |
| itemreform | reform recipes | item reform |
| equipmentproperties | ammo/equip stat ranges | ammo ATK |
| datainfo | identity/sprite tables (acc, job, npc, weapon, robe) | sprites/icons |
| navigation / quest / worldviewdata | maps, quests, world map | trackers/guides |
| effecttool / hateffectinfo / damageskin / cashemotion | pure client visuals | ignore |

## How the tool uses / will use this
- **Names + icons**: `idnum2item*` tables + `iteminfo` resource names → display names and sprite
  resolution.
- **Skill catalog**: `skilltreeview` + `skillid` + `skillinfolist` (+ server damage) → class skill list.
- **Buff/debuff HUD**: `status.yml` Icon ↔ `efstids`/`stateiconinfo` → on-screen buff tracking + OCR.
- **Enchant/reform UI**: server `item_enchant`/`item_reform` + client names → accurate item planning.
