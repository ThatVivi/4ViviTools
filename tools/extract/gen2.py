import json, os, re, yaml
RE="rathena-master/db/re"
def body(fn):
    p=os.path.join(RE,fn)
    if not os.path.isfile(p): return []
    return (yaml.load(open(p,encoding="utf-8"),Loader=yaml.CSafeLoader) or {}).get("Body",[])

RACE={'formless':'Formless','undead':'Undead','brute':'Brute','plant':'Plant','insect':'Insect',
 'fish':'Fish','demon':'Demon','demihuman':'DemiHuman','angel':'Angel','dragon':'Dragon','all':'All'}
ELE={'neutral':'Neutral','water':'Water','earth':'Earth','fire':'Fire','wind':'Wind','poison':'Poison',
 'holy':'Holy','dark':'Shadow','ghost':'Ghost','undead':'Undead','all':'All'}
SIZE={'small':'Small','medium':'Medium','large':'Large','all':'All'}
SIMPLE={'str':'s','agi':'a','vit':'v','int':'i','dex':'d','luk':'l','baseatk':'atk','atk':'atk',
 'matk':'matk','atkrate':'atkp','matkrate':'matkp','hit':'hit','critical':'crit','def':'def',
 'maxhp':'hp','maxsp':'sp','aspdrate':'aspdp'}

def parse_script(s):
    if not s: return []
    mods=[]; agg={}
    for stmt in re.split(r'[;\n]', s):
        stmt=stmt.strip()
        m=re.match(r'bonus\s+b(\w+)\s*,\s*(-?\d+)\s*$', stmt, re.I)
        if m:
            k=m.group(1).lower(); v=int(m.group(2))
            if k=='allstats':
                for kk in 'savidl': agg[kk]=agg.get(kk,0)+v
            elif k in SIMPLE:
                kk=SIMPLE[k]; agg[kk]=agg.get(kk,0)+v
            continue
        m=re.match(r'bonus2\s+b(\w+)\s*,\s*(\w+)\s*,\s*(-?\d+)\s*$', stmt, re.I)
        if m:
            b=m.group(1).lower(); arg=m.group(2).lower(); v=int(m.group(3))
            arg=re.sub(r'^(rc_|ele_|size_)','',arg)
            if b in('addrace','magicaddrace') and arg in RACE: mods.append({'racep':v,'race':RACE[arg]})
            elif b in('addsize','magicaddsize') and arg in SIZE: mods.append({'sizep':v,'size':SIZE[arg]})
            elif b in('addele','magicaddele') and arg in ELE: mods.append({'elep':v,'ele':ELE[arg]})
            elif b=='ignoredefracerate' and arg in RACE: mods.append({'idefrace':v,'race':RACE[arg]})
            continue
    if agg: mods.insert(0,agg)
    return mods

def truthy(d): return [k for k,v in (d or {}).items() if v] if isinstance(d,dict) else []

eq=[]
for e in body("item_db_equip.yml"):
    loc=truthy(e.get("Locations",{}))
    eq.append({"id":e.get("Id",0),"aegis":e.get("AegisName",""),"name":e.get("Name",""),
      "type":e.get("Type",""),"subtype":e.get("SubType",""),"loc":loc,
      "wlvl":e.get("WeaponLevel",0),"atk":e.get("Attack",0),"matk":e.get("MagicAttack",0),
      "def":e.get("Defense",0),"slots":e.get("Slots",0),
      "mods":parse_script((e.get("Script","") or ""))})
cards=[]
for e in body("item_db_etc.yml"):
    if str(e.get("Type","")).lower()!="card": continue
    cards.append({"id":e.get("Id",0),"name":e.get("Name",""),"loc":truthy(e.get("Locations",{})),
      "mods":parse_script((e.get("Script","") or ""))})
items=[]
for src in ("item_db_equip.yml","item_db_etc.yml","item_db_usable.yml"):
    for e in body(src):
        items.append({"id":e.get("Id",0),"aegis":e.get("AegisName",""),"name":e.get("Name",""),
          "type":e.get("Type",""),"slots":e.get("Slots",0),"weight":e.get("Weight",0)})
ench=[{"id":e.get("Id",0),"name":e.get("Option",e.get("Name","")),
       "mods":parse_script((e.get("Script","") or ""))} for e in body("item_randomopt_db.yml")]
mobs=[]
for m in body("mob_db.yml"):
    md=m.get("Modes",{}) or {}
    mobs.append({"id":m.get("Id",0),"aegis":m.get("AegisName",""),"name":m.get("Name",""),
      "level":m.get("Level",1),"hp":m.get("Hp",1),"atk":m.get("Attack",0),"matk":m.get("Attack2",0),
      "def":m.get("Defense",0),"mdef":m.get("MagicDefense",0),"str":m.get("Str",1),"agi":m.get("Agi",1),
      "vit":m.get("Vit",1),"int":m.get("Int",1),"dex":m.get("Dex",1),"luk":m.get("Luk",1),
      "race":m.get("Race",""),"element":m.get("Element",""),"elementLevel":m.get("ElementLevel",1),
      "size":m.get("Size",""),"baseExp":m.get("BaseExp",0),"jobExp":m.get("JobExp",0),
      "mvp":bool(md.get("Mvp",False)),
      "drops":[{"item":d.get("Item",""),"rate":d.get("Rate",0)} for d in (m.get("Drops",[]) or [])]})
sb=body("skill_db.yml")
skills=[{"id":s.get("Id",0),"name":(s.get("Description") or s.get("Name","")),"castMs":0,"delayMs":0,"cooldownMs":0} for s in sb]

data={"equips":eq,"cards":cards,"items":items,"enchants":ench,"mobs":mobs,"skills":skills,"maps":[]}
out="/sessions/keen-amazing-tesla/mnt/4ViviTools/src/4rVivi.Core/Data/gamedata.json"
json.dump(data,open(out,"w",encoding="utf-8"),ensure_ascii=False,separators=(",",":"))
import collections
# sanity: a known race card
def find(name):
    for c in cards:
        if c["name"]==name: return c
    return None
print("size",os.path.getsize(out))
print("equips",len(eq),"cards",len(cards),"mobs",len(mobs),"enchants",len(ench),"skills",len(skills))
print("Hydra Card:", find("Hydra Card"))
print("Strouf Card:", find("Strouf Card"))
print("sample equip w/ mods:", next((e for e in eq if e["mods"] and e["type"]=="Weapon"), {"name":"none"})["name"], next((e for e in eq if e["mods"] and e["type"]=="Weapon"), {}).get("mods"))
nmods=sum(1 for c in cards if c["mods"])
print("cards with parsed mods:",nmods,"/",len(cards))
