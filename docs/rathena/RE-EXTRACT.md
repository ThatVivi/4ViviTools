# Re-Extract Scripts (per topic)

Run these to regenerate or re-verify each doc's data straight from source. Assumes:

```bash
RA=./rathena          # rAthena clone (has src/ and db/re/)
RE=$RA/db/re
SRC=$RA/src/map
GRF=./system          # extracted client System/ + luafiles514/ contents
```

Full data pipeline (produces `src/4rVivi.Core/Data/gamedata.json`) lives in `tools/extract/`:
`gen2.py` (equips/cards/items/mobs/enchants + script→mods), `combos.py` (item combos),
`skills_gen.py` (skill catalog per class), `ratios.py` (per-skill multipliers). Run in that order.

---

### damage-formula.md
```bash
sed -n '/battle_calc_attack_skill_ratio/,/^}/p' $SRC/battle.cpp        # skill ratio entry
grep -nE "status_base_atk|battle_calc_sizefix|RE_LVL_DMOD|attr_fix" $SRC/battle.cpp
```

### stats-substats.md (status_calc_bl_main: patk/smatk/hit/flee/def2…)
```bash
grep -nE "status->patk|status->smatk|status->hit|status->flee|status->def2|status->mdef2|status->res|status->mres|status->hplus|status->crate|status->cri" $SRC/status.cpp
sed -n '/unsigned short status_base_atk/,/return/p' $SRC/status.cpp        # StatusATK incl. 5*POW
```

### elements.md
```bash
grep -vE '^\s*#|^\s*$' $RE/attr_fix.yml | sed -n '1,120p'   # element modifier table
```

### refine.md (weapon refine ATK = Bonus/100)
```bash
python3 - <<'PY'
import yaml
d=yaml.safe_load(open("./rathena/db/re/refine.yml"))
for g in d['Body']:
  if g['Group']!='Weapon': continue
  for lv in g['Levels']:
    print('WLv',lv['Level'],[rl.get('Bonus',0)//100 for rl in lv['RefineLevels']])
PY
grep -n "wa->atk2 += info->bonus / 100" ./rathena/src/map/status.cpp   # how it's applied
```

### item-bonuses.md (script grammar) — see tools/extract/gen2.py `parse_script()`
```bash
grep -nE "bAddRace|bAddEle|bAddSize|bAtkRate|bBaseAtk|bMatk" $RA/doc/item_bonus.txt | head
```

### item-combos.md — tools/extract/combos.py
```bash
sed -n '/Body:/,/Combo:/p' $RE/item_combos.yml | head
```

### weapon-types.md
```bash
sed -n '/enum weapon_type/,/};/p' $SRC/pc.hpp
sed -n '/void pc_calcweapontype/,/^}/p' $SRC/pc.cpp     # dual-wield
sed -n '/pc_equippoint_sub/,/return ep/p' $SRC/pc.cpp   # who can dual-wield
```

### monsters.md
```bash
sed -n '/AegisName: PORING/,/Modes:/p' $RE/mob_db.yml    # one mob's full fields
```

### classes.md
```bash
grep -E "JT_NOVICE|JT_SWORDMAN|JT_KNIGHT" $GRF/jobidentity.lub
sed -n '/SKILL_TREEVIEW_FOR_JOB/,/JT_MAGICIAN/p' $GRF/skilltreeview.lub
```

### skills.md (catalog + multipliers) — tools/extract/skills_gen.py + ratios.py
```bash
sed -n '/calculateSkillRatio/,/^}/p' $SRC/skills/swordman/bash.cpp     # one skill's ratio
grep -rhE "case [A-Z0-9_]+:|make_unique<Skill" $SRC/skills/swordman/skill_factory_swordman.cpp | head
awk '/Name: SM_BASH/{f=1} f{print} f&&/HitCount/{print;exit}' $RE/skill_db.yml   # hits/element/type
```

### aspd.md
```bash
grep -nE "BaseASPD|status_calc_aspd\b" $RE/job_basepoints.yml $SRC/status.cpp | head
sed -n '/int16 status_calc_aspd\(/,/return/p' $SRC/status.cpp
```

### size-modifier.md
```bash
grep -vE '^\s*#|^\s*$' $RE/size_fix.yml                    # renewal overrides only
grep -n "battle_calc_sizefix" $SRC/battle.cpp
```

### status-effects.md
```bash
grep -cE "^\s*SC_[A-Z]" $SRC/status.hpp                    # count of SC_*
grep -nE "Status:|Icon: EFST_" $RE/status.yml | head -20   # SC -> EFST icon
```

### hp-sp.md
```bash
grep -nE "HpFactor|HpIncrease|SpFactor|SpIncrease|MaxStats|BaseASPD" $RE/job_basepoints.yml | head
```

### mounts.md
```bash
grep -nE "OPTION_(RIDING|DRAGON|WUG|WUGRIDER|MADOGEAR|MOUNTING|FALCON)" $SRC/pc.hpp
```

### ammo-arrows.md
```bash
grep -nE "e_ammo_type|AMMO_" $SRC/itemdb.hpp | head
grep -E 'Type: Ammo' -A3 $RE/item_db_equip.yml | head
```

### maps-woe-pvp.md
```bash
grep -cE "MF_[A-Z]" $SRC/map.hpp                           # mapflag count
grep -nE "mf_pvp|mf_gvg|mf_restricted|mf_nopvp" $SRC/*.cpp | head
```

### level-penalty.md
```bash
grep -nE "Type:|Difference:|Rate:" $RE/level_penalty.yml | head -20
```

### client-grf-data.md / client-systems.md (the GRF zip)
```bash
unzip -l luafiles514.zip | grep -iE "skillinfolist|skilltreeview|skillid|iteminfo|efstids|itemreform|equipmentproperties|addrandomoption"
unzip -o -j luafiles514.zip "luafiles514/lua files/skillinfoz/*" -d ./out
file ./out/*.lub        # text vs 'LuaQ' compiled bytecode
head -c 6 system/iteminfo.lub   # compiled -> use idnum2item*.txt instead
```

### db-files.md
```bash
ls $RE/*.yml $RE/*.txt
```

### original-4rtools.md (C# WinForms reference)
```bash
unzip -l 4RTools-main.zip | grep -E "Forms/|Model/|Utils/"
unzip -o -j 4RTools-main.zip "4RTools-main/Model/Autopot.cs" "4RTools-main/Utils/EffectStatusIDs.cs" "4RTools-main/Utils/ProcessMemoryReader.cs" -d ./out
```

### original-ro-tools.md (Python/PyQt reference)
```bash
cd ro-tools-main && ls events/ game/ service/ gui/widget/
sed -n '1,30p' game/map_buffs.py     # status-id -> buff name dicts
sed -n '1,40p' service/memory.py      # memory read + offsets
```
