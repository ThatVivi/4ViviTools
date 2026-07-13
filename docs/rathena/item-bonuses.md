# Item Bonuses (Scripts)

> **Re-extract:** parser is `tools/extract/gen2.py` `parse_script()`; `grep -nE "bAddRace|bAddEle|bAddSize|bAtkRate" $RA/doc/item_bonus.txt`. See [RE-EXTRACT.md](RE-EXTRACT.md).

Source: `doc/item_bonus.txt`, item `Script:` fields in `db/re/item_db_*.yml`, run by `src/map/script.cpp`.

Every gear/card/enchant effect is a **script** of `bonus` calls executed on equip. This is the only
way items change stats/damage — there is no separate "stats" column.

## Forms
- `bonus bType,val;` — single arg (e.g. `bonus bStr,10;`)
- `bonus2 bType,arg,val;` — typed (e.g. `bonus2 bAddRace,RC_Brute,30;`)
- `bonus3 bType,a,b,val;` — e.g. autocast, conditional adds.

## Damage-relevant bonus types

| Bonus | Effect |
|-------|--------|
| `bStr/bAgi/bVit/bInt/bDex/bLuk` | flat stat |
| `bAllStats` | +N to all six |
| `bAtk` / `bBaseAtk` | flat ATK (ExtraATK) |
| `bAtkRate` | ATK% (Group A) |
| `bMatk` / `bMatkRate` | flat / % MATK |
| `bHit`, `bCritical`, `bFlee`, `bAspd`, `bAspdRate` | sub-stats |
| `bonus2 bAddRace,RC_x,n` | +n% physical vs race |
| `bonus2 bMagicAddRace,RC_x,n` | +n% magic vs race |
| `bonus2 bAddSize,Size_x,n` | +n% vs size |
| `bonus2 bAddEle,Ele_x,n` | +n% vs element |
| `bonus2 bIgnoreDefRaceRate,RC_x,n` | ignore n% DEF vs race |
| `bonus2 bSubRace/bSubEle/bSubSize` | damage **reduction** taken (defense) |

## Constants
- Race: `RC_Formless, RC_Undead, RC_Brute, RC_Plant, RC_Insect, RC_Fish, RC_Demon, RC_DemiHuman, RC_Angel, RC_Dragon, RC_Player_*, RC_All`.
- Element: `Ele_Neutral, Ele_Water, Ele_Earth, Ele_Fire, Ele_Wind, Ele_Poison, Ele_Holy, Ele_Dark (=Shadow), Ele_Ghost, Ele_Undead, Ele_All`.
- Size: `Size_Small, Size_Medium, Size_Large, Size_All`.

## Conditional / scaled scripts
Many scripts use `.@r = getrefine();`, `if (BaseLevel >= X)`, `eaclass()` etc. These are **not** plain
constants. The parser currently handles unconditional numeric bonuses; refine-scaled and conditional
blocks are a known follow-up.

## Tool mapping
`tools/build_gamedata.py` → `parse_script()` converts scripts into compact `mods` entries
(`s,a,v,i,d,l, atk, matk, atkp, racep+race, sizep+size, elep+ele`). `CalculatorViewModel.ToGear()`
turns each into a `GearBonus`; race/size/element entries apply only vs the matching target.
