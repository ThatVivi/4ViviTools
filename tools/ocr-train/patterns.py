"""Training-text patterns for the OCR synthetic generator.

Numeric roles emit random numbers/pairs; name roles sample from the game-vocabulary corpus files in
corpus/ (generated from gamedata: monsters, items, skills, maps, classes, HUD strings, character
movements, hair/appearance). synth.py renders each in the RO fonts. See docs/rathena/ocr.md.
"""
import os, random

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")

def _load(name):
    p = os.path.join(CORPUS, name)
    try:
        with open(p, encoding="utf-8") as f:
            return [ln.strip() for ln in f if ln.strip()]
    except Exception:
        return []

# Loaded once. Empty list -> role contributes a harmless placeholder.
_C = {
    "Monster":   _load("monsters.txt"),
    "ItemName":  _load("items.txt"),
    "SkillName": _load("skills.txt"),
    "MapName":   _load("maps.txt"),
    "ClassName": _load("classes.txt"),
    "HUD":       _load("hud.txt"),
    "Movement":  _load("movements.txt"),
    "HairStyle": _load("hairstyles.txt"),
}

def _num(lo, hi): return str(random.randint(lo, hi))
def _pair(lo, hi):
    mx = random.randint(lo, hi); cur = random.randint(0, mx)
    sep = random.choice(["/", " / ", " /", "/ "])   # RO shows "140 / 266"
    return f"{cur}{sep}{mx}"
def _pct(): return f"{random.randint(0,100)}%"
def _lvl(lo, hi, label=""):
    n = random.randint(lo, hi)
    return random.choice([str(n), f"Lv. {n}", f"{label} Lv. {n}".strip(), f"{label} Lv.{n}".strip()])
def _pick(key, fallback="Unknown"):
    lst = _C.get(key) or []
    return random.choice(lst) if lst else fallback

ROLE_PATTERNS = {
    # numeric / stat readouts
    "HP": lambda: _pair(50, 999999),
    "SP": lambda: _pair(10, 99999),
    "BaseLevel": lambda: _lvl(1, 250, "Base"),
    "JobLevel": lambda: _lvl(1, 70, "Job"),
    "Zeny": lambda: _num(0, 2000000000),
    "Weight": lambda: _pair(0, 30000),
    "Percent": lambda: _pct(),
    "ExpBar": lambda: _pct(),
    "PosX": lambda: _num(0, 400),
    "PosY": lambda: _num(0, 400),
    "CharName": lambda: random.choice(["Vivi", "DarkLord", "Aizen", "ProtoVivi", "GMVivi", "Eldrynn"]),
    # game-vocabulary roles (full corpus)
    "ClassName": lambda: _pick("ClassName", "Novice"),
    "Monster":   lambda: _pick("Monster", "Poring"),
    "ItemName":  lambda: _pick("ItemName", "Red Potion"),
    "SkillName": lambda: _pick("SkillName", "Bash"),
    "MapName":   lambda: _pick("MapName", "prontera"),
    "HUD":       lambda: _pick("HUD", "Lv"),
    "Movement":  lambda: _pick("Movement", "Standing"),
    "HairStyle": lambda: _pick("HairStyle", "Hair Style"),
}

def sample_value(role: str) -> str:
    fn = ROLE_PATTERNS.get(role)
    return fn() if fn else role
