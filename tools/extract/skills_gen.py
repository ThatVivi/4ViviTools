import json,re,yaml
RE="/sessions/keen-amazing-tesla/mnt/outputs/ratx/rathena-master/db/re"
sb=yaml.load(open(RE+"/skill_db.yml",encoding="utf-8"),Loader=yaml.CSafeLoader)["Body"]

def hitcount(s):
    h=s.get("HitCount",1)
    if isinstance(h,list): h=h[0].get("Count",1) if h and isinstance(h[0],dict) else 1
    try: return int(h)
    except: return 1

# server skill meta by AEGIS name and by display name
meta_by_aegis={}; skills=[]
for s in sb:
    aeg=s.get("Name",""); name=s.get("Description") or aeg
    typ=s.get("Type","")           # Weapon/Magic/Misc
    tgt=s.get("TargetType","")
    ele=s.get("Element","Weapon")
    hits=hitcount(s)
    offensive = (typ in ("Weapon","Magic")) and (tgt in ("Attack",""))
    rec={"id":s.get("Id",0),"name":name,"aegis":aeg,"hits":hits,
         "element":(ele if isinstance(ele,str) else "Weapon"),
         "type":typ,"magic":typ=="Magic","atk":bool(offensive),
         "castMs":0,"delayMs":0,"cooldownMs":0}
    skills.append(rec); meta_by_aegis[aeg]=rec

# client SKID name->id and job tree
g="/sessions/keen-amazing-tesla/mnt/outputs/grf"
skid={}
for m in re.finditer(r'(\w+)\s*=\s*(\d+)', open(g+"/skillid.lub",encoding="latin-1").read()):
    skid[m.group(1)]=int(m.group(2))
jobid={}
for m in re.finditer(r'(JT_\w+)\s*=\s*(\d+)', open(g+"/jobidentity.lub",encoding="latin-1").read()):
    jobid[m.group(2)]=m.group(1)

tv=open(g+"/skilltreeview.lub",encoding="latin-1").read()
# parse blocks: [JOBID.JT_X] = { ... SKID.skill ... }
catalog={}
for jm in re.finditer(r'\[JOBID\.(JT_\w+)\]\s*=\s*\{(.*?)\n\t\}', tv, re.S):
    job=jm.group(1); body=jm.group(2)
    aegs=re.findall(r'SKID\.(\w+)', body)
    cls=job[3:].replace("_"," ").title()  # JT_LORD_KNIGHT -> "Lord Knight"
    names=[]
    for a in aegs:
        r=meta_by_aegis.get(a)
        if r and r["atk"]: names.append(r["name"])
    if names: catalog.setdefault(cls,[])
    for n in names:
        if n not in catalog.get(cls,[]): catalog[cls].append(n)

out="/sessions/keen-amazing-tesla/mnt/4ViviTools/src/4rVivi.Core/Data/gamedata.json"
d=json.load(open(out,encoding="utf-8"))
d["skills"]=skills
d["skillCatalog"]=catalog
json.dump(d,open(out,"w",encoding="utf-8"),ensure_ascii=False,separators=(",",":"))
import os
print("skills",len(skills),"offensive",sum(1 for s in skills if s["atk"]))
print("catalog classes",len(catalog))
print("Knight sample:",catalog.get("Knight"))
print("Rune Knight sample:",catalog.get("Rune Knight"))
print("size",os.path.getsize(out))
