# Client / GRF Data (System folder) ↔ rAthena

> **Re-extract:** `unzip -l luafiles514.zip | grep -iE "skillinfolist|skilltreeview|iteminfo|idnum2item"` ; `file $GRF/*.lub` (text vs LuaQ). See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: the client `System/` folder (a.k.a. "GRF INFO") + `luafiles514.zip`. These are **client-side**
files. rAthena is the **server**: it holds IDs and mechanics; the client holds **names, descriptions,
sprites, and UI layout**. The two are linked by **numeric ID** (item id, skill id, job id).

## The bridge (how server ↔ client connect)

| Concept | Server (rAthena `db/re`) | Client (`System/`) | Link |
|---------|--------------------------|--------------------|------|
| Item | `item_db_*.yml` (AegisName, Type, Script, ATK…) | `iteminfo.lub`, `idnum2item*table.txt` (display name, desc, **sprite resource**, slot count) | **item id** |
| Skill | `skill_db.yml` (cast/delay/hit/element), `battle_calc_skillratio` (damage) | `skillinfolist.lub`, `skillid.lub`, `skilldescript.lub` (name, SP, range, maxlv, desc) | **skill id / SKID** |
| Skill tree | `skill_tree.yml` (learnable per job) | `skilltreeview.lub` (UI tree per job) | **job id + skill id** |
| Job | job constants (`MAPID_*`) | `jobname.lub`, `jobidentity.lub`, `pcjobname.lub` (sprite names) | **job id** |
| Random option | `item_randomopt_db.yml` (script) | `addrandomoptionnametable.lub` (display name) | **option id** |
| Map | map files | `mapnametable.txt` (display name) | **map name** |
| Card | (item type Card) | `carditemnametable.txt`, `cardprefixnametable.txt` | **item id** |

## Key client files (System/)
- **iteminfo.lub** (3.4 MB) — per item id: `identifiedDisplayName`, `identifiedResourceName`
  (the **sprite/icon** name), `identifiedDescriptionName`, unidentified variants, `slotCount`,
  `ClassNum` (view id for headgear). The authoritative client item DB.
- **idnum2itemdisplaynametable.txt / idnum2itemresnametable.txt / idnum2itemdesctable.txt** — older
  flat-table equivalents (id → name / resource / description).
- **itemslotcounttable.txt**, **itemslottable.txt** — slot counts.
- **accessoryid.lub / accname.lub** — headgear sprite ids ↔ names.
- **mapnametable.txt** — internal map name → display name.
- **skillnametable.txt / skilldesctable.txt** — skill display names + descriptions.
- **msgstringtable.txt** — every UI string (used for localisation and OCR text matching).

## luafiles514.zip → `lua files/skillinfoz/`
- **skillid.lub** — `SKID = { NV_BASIC = 1, SM_BASH = 5, … }` (AEGIS → skill id).
- **skillinfolist.lub** — `SKILL_INFO_LIST[SKID.x] = { "AEGIS", SkillName, MaxLv, SpAmount[lv],
  AttackRange[lv], bSeperateLv, … }`. Client per-skill metadata (no damage ratio — that's server-side).
- **skilltreeview.lub** — `SKILL_TREEVIEW_FOR_JOB[JOBID.JT_x] = { [pos] = SKID.skill, … }`. **The
  class → skills mapping** (83 jobs). Higher jobs inherit lower trees via **jobinheritlist.lub**.
- **skilldelaylist.lub** — per-skill delay; **skilldescript.lub** — long descriptions.

## Why this matters for the tool
1. **Proper display names** — `iteminfo.lub` / `idnum2item*` give the exact in-client names (and
   slot count), which can differ from rAthena AegisNames. Best source for the calculator's item lists.
2. **Icons by resource name** — `identifiedResourceName` is the sprite file name in the GRF; this is
   how the icon service can resolve any item's sprite (not just by id).
3. **Skill catalog per class** — `skilltreeview.lub` + `skillid.lub` + `skillinfolist.lub` (+ server
   `skill_db.yml` for timing, `battle_calc_skillratio` for damage) = the full "pick class → its
   skills, with SP/range/hits/element" feature.
4. **Enchant names** — `addrandomoptionnametable.lub` gives readable random-option names to pair with
   `item_randomopt_db.yml` scripts.
5. **OCR ground truth** — `idnum2itemdisplaynametable.txt`, `skillnametable.txt`, and
   `msgstringtable.txt` are the exact on-screen strings, so they make an ideal **dictionary for OCR
   correction** (snap a fuzzy OCR read to the nearest real item/skill/map name).

## Tool mapping (current vs planned)
- Current: the calculator uses rAthena names + the GRF icon pipeline (`IconImageService`).
- Planned: load `iteminfo.lub` resource names for icon resolution; build the class→skill catalog from
  `skilltreeview.lub`; use the display-name tables as an OCR correction dictionary.
