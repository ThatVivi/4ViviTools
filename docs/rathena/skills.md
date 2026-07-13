# Skills

> **Re-extract:** `tools/extract/skills_gen.py` (catalog) + `ratios.py` (multipliers); raw: `sed -n '/calculateSkillRatio/,/^}/p' $SRC/skills/swordman/bash.cpp`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/skill_db.yml` (metadata), `src/map/battle.cpp` (`battle_calc_skillratio` — the damage %),
`src/map/skill.cpp` (`skill_attack`), `db/re/skill_tree.yml` (job → skills).

## What skill_db.yml stores (per skill)
| Field | Meaning |
|-------|---------|
| Name / Description | AEGIS name / readable name |
| MaxLevel | levels |
| Type | Weapon / Magic / Misc |
| TargetType | Attack / Self / Support / Ground / Trap |
| Range | cell range (−1 = weapon range) |
| Hit | Single / Multi_Hit |
| **HitCount** | number of hits |
| **Element** | `Weapon` (uses weapon/ammo element) or a fixed element (Fire/Holy/…) |
| Requires | SP cost, allowed **Weapon** types, **Ammo**, **State**, items, zeny |
| CastTime / AfterCastDelay / Cooldown | timing (per level) |

## Where the damage RATIO lives (updated finding)
The per-skill **damage multiplier** is NOT in skill_db. In **current rAthena** it was moved out of the
old `battle_calc_skillratio` switch into **one C++ class per skill** under
`src/map/skills/<class>/<skill>.cpp`, each with a `calculateSkillRatio()` that mutates
`base_skillratio` (which starts at 100). Example — `skills/swordman/bash.cpp`:
```cpp
void SkillBash::calculateSkillRatio(...) const { base_skillratio += 30 * skill_lv; } // 100% + 30%/lv
```
The factory `skills/<class>/skill_factory_<class>.cpp` maps `case SM_BASH: return make_unique<SkillBash>()`.
So: **factory** gives skill-id → class, the **class file** gives the ratio formula. The tool extracts
these (at max level) into `gamedata.json` `skills[].mult`, so Bash auto-fills 400%.

## Element source
- `Element: Weapon` → the hit uses the weapon's (or ammo's, or endow) element.
- Fixed element skills (e.g. Soul Strike = Ghost, Fire Bolt = Fire) ignore weapon element.

## Class → skill mapping
`skill_tree.yml` lists, per job, which skills are learnable and their prerequisites — this is the data
behind a "pick class → show its skills" dropdown.

## Tool mapping
The calculator's Damaging-Skill field takes a skill name + manual multiplier + hit count + magic flag.
The **class-filtered skill catalog with built-in multipliers/element** is the next major pass:
`skill_tree.yml` gives the class filtering; the multipliers come from `battle_calc_skillratio`.
