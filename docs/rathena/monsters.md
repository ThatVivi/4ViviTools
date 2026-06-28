# Monsters (mob_db)

> **Re-extract:** `sed -n '/AegisName: PORING/,/Modes:/p' $RE/mob_db.yml` (one mob's full fields). See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `db/re/mob_db.yml`, `src/map/mob.cpp`, `src/map/status.cpp` (mob stat calc).

## Key fields used by the tool
| Field | Meaning |
|-------|---------|
| Id / AegisName / Name | identity |
| Level, Hp | level and max HP |
| Attack / Attack2 | min/base ATK and MATK |
| Defense / MagicDefense | hard DEF / MDEF |
| Str/Agi/Vit/Int/Dex/Luk | stats (feed soft def/flee/hit) |
| Race | Formless/Undead/Brute/Plant/Insect/Fish/Demon/DemiHuman/Angel/Dragon |
| Element + ElementLevel | defensive element and level (1–4) |
| Size | Small/Medium/Large |
| BaseExp / JobExp | experience |
| Modes.Mvp | MVP flag |
| Drops | item + rate (×10000) |

## MVP
MVPs have `Modes: { Mvp: true }`, MVP drops, and an MVP reward. The tracker lists them, shows the
best attack element (from the mob's defensive element via attr_fix), and respawn windows.

## Defense vs damage
A mob's **Defense** is hard DEF (curve reduction), its **Str/Agi/Vit/Int/Dex/Luk** drive soft
DEF2/MDEF2/FLEE. Its **Element + level** decide your element modifier (so pick the best attack
element). **Size** matters for weapon size modifier and Size% cards.

## Tool mapping
`MobInfo` carries all of the above. The calculator's enemy search auto-fills Level, HP, DEF, MDEF,
AGI/VIT/INT/DEX/LUK, element(+level), race, size on pick. The MVP tracker uses element + respawn.
