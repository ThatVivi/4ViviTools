# Faithful Replica Blueprint — 4RTools & ro-tools tabs (exact controls)

> **Re-extract:** 4RTools `unzip -o -j 4RTools-main.zip "4RTools-main/Forms/*.Designer.cs" -d out && grep -oE 'Text = \"[^\"]*\"' out/*.Designer.cs` ; ro-tools `grep -rE "setText|QLabel|QCheckBox" ro-tools-main/gui/widget/`.

The control-by-control spec to rebuild each tab 1:1. **Difference from the originals: memory reads →
OCR (LiveStats).** Build order is tab-by-tab; ✅ = done in our shell, ◻ = remaining.

## 4RTools (light WinForms) → `FourRToolsShellView`

| Tab | Exact controls (from `*.Designer.cs`) | Status |
|-----|----------------------------------------|--------|
| **Autopot** | `HP` [key][% spin], `SP` [key][% spin], `Delay` [ms] | ✅ |
| **Autobuff – Skills** | rows: [on][skill key][recast ms] | ✅ |
| **Autobuff – Stuffs** | rows: [on][item key][recast ms] | ✅ |
| **Skill Spammer** | class combo, delay, skill rows [icon][on][name][key] | ✅ |
| **ATK × DEF** | `ATK Switch` [key], `DEF Switch` [key], `Switch Delay`, `Spammer Key`, `Spammer Delay`, ☐ `With mouse click` (group "Configuration") | ◻ make faithful |
| **Debuff Recovery** | group **Status Effects**: `Status` combo ×N + `Cooldown` ms; group **Status Recovery**; ☐ `Enable Auto Stand (when forced to sit)`; group "Elvira Candy" | ◻ |
| **Macro Switch** | macro rows: name, keys, interval, loop | ✅ basic |
| **Macro Songs** | ensemble/song sequence rows | ✅ basic |
| **Skill Timers** | per-skill cooldown rows | ✅ |
| **Servers / Profile** | server list, profile name | (System tabs cover this) |

## ro-tools (dark PyQt, Portuguese) → `RoToolsShellView`

| Tab (panel) | Exact controls (PT → EN) | Status |
|-------------|---------------------------|--------|
| **Auto HP/SP** (`painel_auto_item_hp_sp`) | HP [key][%], SP [key][%], ☐ "Bloquear uso em cidades?" (Block in towns) | ✅ core |
| **Item Buff** (`painel_auto_item_buff`) | buff item rows [item][key], block-in-towns | ✅ core |
| **Item Debuff** (`auto_item_debuff`) | cure item per debuff | ✅ core |
| **Auto Element** (`painel_auto_element`) | element/endow macro [name][key] | ✅ core |
| **Skill Buff / Equip** (`skill_buff`,`skill_equip`) | skill recast rows; equip-swap buffs | ✅ core |
| **Skill Spammer** (`skill_spawmmer`) | class skills [icon][on][key], delay | ✅ |
| **Macro / Hotkey** (`macro_event`,`hotkey_event`) | macro rows, keybinds | ✅ core |
| **Bot** (`painel_bot`) | `Status`, ☐ steps, `Passo` (step), `Alcance` (range), `Kite min`, `Fuga` (flee), `Anti-KS`, `Timeout atk`, `Seleção` (target select), `Asa por HP <` (fly-wing below HP%) | ◻ partial (no pathfinding) |
| **Utilities** | auto teleport, auto ygg, abracadabra, halter lead, auto commands | ◻ |

## Shared mechanics (both tools)
- **HP/SP %** triggers → ours from OCR HP/SP.
- **Status detection** via EFST/status IDs (`EffectStatusIDs.cs` / `game/map_buffs.py`) → see
  [original-4rtools.md](original-4rtools.md), [original-ro-tools.md](original-ro-tools.md),
  [status-effects.md](status-effects.md). Ours: planned status-bar OCR.
- **Input** via global key send; **per-server profiles**.

## What we deliberately do NOT port
Process-memory reader, AHK external bridge, client auto-patcher/updater, ad/advertisement form (4RTools);
the A* **pathfinding bot** (`map_gat` + `bot_pathfinder`) of ro-tools. These either conflict with the
OCR-only design or are out of scope. Everything else is rebuilt tab-by-tab.
