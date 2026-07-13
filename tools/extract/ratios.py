import json,re,os,glob,yaml
SRC="rathena-master/src/map/skills"
RE="rathena-master/db/re"

# 1) aegis -> SkillClass from factories
aegis2class={}
for fac in glob.glob(SRC+"/*/skill_factory_*.cpp"):
    txt=open(fac,encoding="latin-1").read()
    pend=[]
    for line in txt.splitlines():
        m=re.search(r'case\s+([A-Z0-9_]+)\s*:', line)
        if m: pend.append(m.group(1)); continue
        m=re.search(r'make_unique<\s*(Skill[A-Za-z0-9_]+)\s*>', line)
        if m:
            for a in pend: aegis2class[a]=m.group(1)
            pend=[]
        elif 'return' in line: pend=[]

# 2) class -> calculateSkillRatio body
class2body={}
for cpp in glob.glob(SRC+"/*/*.cpp"):
    txt=open(cpp,encoding="latin-1").read()
    for m in re.finditer(r'(Skill[A-Za-z0-9_]+)::calculateSkillRatio\s*\([^)]*\)\s*const\s*\{', txt):
        cls=m.group(1); start=m.end(); depth=1; i=start
        while i<len(txt) and depth>0:
            if txt[i]=='{':depth+=1
            elif txt[i]=='}':depth-=1
            i+=1
        class2body[cls]=txt[start:i]

SAFE=re.compile(r'^[\d\s\+\-\*/\(\)]+$')
def ratio_at(body, lv):
    base=100
    for op,expr in re.findall(r'base_skillratio\s*(\+=|=)\s*([^;]+);', body):
        e=expr.replace('skill_lv',str(lv)).strip()
        if not SAFE.match(e): 
            continue
        try: v=int(eval(e))
        except: continue
        if op=='=': base=v
        else: base+=v
    return base

# 3) skill_db: aegis -> (name, maxlv)
sb=yaml.load(open(RE+"/skill_db.yml",encoding="utf-8"),Loader=yaml.CSafeLoader)["Body"]
aegis2name={s.get("Name",""):(s.get("Description") or s.get("Name","")) for s in sb}
aegis2max={s.get("Name",""):(s.get("MaxLevel",1) or 1) for s in sb}

# 4) name -> multiplier
name2mult={}
got=0
for aegis,cls in aegis2class.items():
    body=class2body.get(cls)
    if not body: continue
    lv=aegis2max.get(aegis,10) or 10
    r=ratio_at(body,lv)
    nm=aegis2name.get(aegis)
    if nm: name2mult[nm]=round(r/100.0,2); got+=1

# 5) merge into gamedata
out="/sessions/keen-amazing-tesla/mnt/4ViviTools/src/4rVivi.Core/Data/gamedata.json"
d=json.load(open(out,encoding="utf-8"))
n=0
for s in d["skills"]:
    if s["name"] in name2mult:
        s["mult"]=name2mult[s["name"]]; n+=1
    else:
        s["mult"]=1.0
json.dump(d,open(out,"w",encoding="utf-8"),ensure_ascii=False,separators=(",",":"))
print("aegis2class",len(aegis2class),"bodies",len(class2body),"mapped mult",got,"applied",n)
print("Bash:",name2mult.get("Bash"),"Pierce:",name2mult.get("Pierce"),"Bowling Bash:",name2mult.get("Bowling Bash"),"Sonic Blow:",name2mult.get("Sonic Blow"),"Double Strafe:",name2mult.get("Double Strafe"))
