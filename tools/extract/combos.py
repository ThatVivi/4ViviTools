import json,re,yaml
RACE={'formless':'Formless','undead':'Undead','brute':'Brute','plant':'Plant','insect':'Insect','fish':'Fish','demon':'Demon','demihuman':'DemiHuman','angel':'Angel','dragon':'Dragon','all':'All'}
ELE={'neutral':'Neutral','water':'Water','earth':'Earth','fire':'Fire','wind':'Wind','poison':'Poison','holy':'Holy','dark':'Shadow','ghost':'Ghost','undead':'Undead','all':'All'}
SIZE={'small':'Small','medium':'Medium','large':'Large','all':'All'}
SIMPLE={'str':'s','agi':'a','vit':'v','int':'i','dex':'d','luk':'l','baseatk':'atk','atk':'atk','matk':'matk','atkrate':'atkp','matkrate':'matkp','hit':'hit','critical':'crit','def':'def','maxhp':'hp','maxsp':'sp','aspdrate':'aspdp'}
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
            elif k in SIMPLE: agg[SIMPLE[k]]=agg.get(SIMPLE[k],0)+v
            continue
        m=re.match(r'bonus2\s+b(\w+)\s*,\s*(\w+)\s*,\s*(-?\d+)\s*$', stmt, re.I)
        if m:
            b=m.group(1).lower(); arg=re.sub(r'^(rc_|ele_|size_)','',m.group(2).lower()); v=int(m.group(3))
            if b in('addrace','magicaddrace') and arg in RACE: mods.append({'racep':v,'race':RACE[arg]})
            elif b in('addsize','magicaddsize') and arg in SIZE: mods.append({'sizep':v,'size':SIZE[arg]})
            elif b in('addele','magicaddele') and arg in ELE: mods.append({'elep':v,'ele':ELE[arg]})
    if agg: mods.insert(0,agg)
    return mods

out="/sessions/keen-amazing-tesla/mnt/4ViviTools/src/4rVivi.Core/Data/gamedata.json"
d=json.load(open(out,encoding="utf-8"))
# aegis -> display name from equips + items
a2n={}
for e in d['equips']: a2n[e['aegis']]=e['name']
for it in d['items']:
    if it.get('aegis') and it['aegis'] not in a2n: a2n[it['aegis']]=it['name']

cb=yaml.load(open("rathena-master/db/re/item_combos.yml",encoding="utf-8"),Loader=yaml.CSafeLoader).get("Body",[])
combos=[]
for entry in cb:
    mods=parse_script(entry.get("Script","") or "")
    if not mods: continue
    sets=[]
    for c in entry.get("Combos",[]):
        names=[a2n.get(a) for a in c.get("Combo",[])]
        if all(names) and len(names)>=2: sets.append(names)
    if sets: combos.append({"sets":sets,"mods":mods})
d['combos']=combos
json.dump(d,open(out,"w",encoding="utf-8"),ensure_ascii=False,separators=(",",":"))
print("combos with damage mods:",len(combos))
print("sample:",combos[0] if combos else None)
import os; print("size",os.path.getsize(out))
