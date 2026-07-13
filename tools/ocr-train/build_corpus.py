#!/usr/bin/env python3
"""
build_corpus.py — regenerate the OCR training vocabulary (corpus/*.txt) from gamedata.json.

The synthetic generator (synth.py + patterns.py) renders these strings in the RO fonts so the model
learns the real in-game words: monsters, items, skills, maps, classes, HUD strings, character
movements/states, and appearance terms. Run whenever gamedata.json changes.

Usage:
    python tools/ocr-train/build_corpus.py --gamedata ../../src/4rVivi.Core/Data/gamedata.json
"""
import argparse, glob, json, os, re

CLASSES = ["Novice","Swordman","Knight","Lord Knight","Rune Knight","Dragon Knight","Crusader","Paladin","Royal Guard","Imperial Guard","Mage","Wizard","High Wizard","Warlock","Archmage","Sage","Professor","Sorcerer","Elemental Master","Archer","Hunter","Sniper","Ranger","Windhawk","Bard","Clown","Minstrel","Troubadour","Dancer","Gypsy","Wanderer","Trouvere","Acolyte","Priest","High Priest","Arch Bishop","Cardinal","Monk","Champion","Sura","Inquisitor","Merchant","Blacksmith","Whitesmith","Mechanic","Meister","Alchemist","Creator","Genetic","Biolo","Thief","Assassin","Assassin Cross","Guillotine Cross","Shadow Cross","Rogue","Stalker","Shadow Chaser","Abyss Chaser","Super Novice","Hyper Novice","Gunslinger","Rebellion","Night Watch","Ninja","Kagerou","Oboro","Shinkiro","Shiranui","Taekwon","Star Gladiator","Star Emperor","Sky Emperor","Soul Linker","Soul Reaper","Soul Ascetic","Summoner","Spirit Handler","Doram"]
HUD = ["Lv","HP","SP","AP","Base","Job","EXP","Weight","Zeny","Atk","Matk","Def","Mdef","Hit","Flee","Crit","Aspd","Str","Agi","Vit","Int","Dex","Luk","Pow","Sta","Wis","Spl","Con","Crt","Cast","Delay","Cooldown","Range","Weapon","Armor","Shield","Garment","Shoes","Accessory","Headgear","Refine","Slot","Card","Enchant","Map","Party","Guild","Storage","Cart","Inventory","Equip","Status","Skill","Quest","Mail","Vending","Shop","Trade","Whisper","Poison","Curse","Stun","Freeze","Sleep","Blind","Silence","Stone","Bleeding","Confusion","Provoke","Blessing","Increase AGI","Endure","Concentration","Magnificat","Gloria","Kyrie Eleison","Assumptio","Quagmire","Decrease AGI"]
MOVES = ["Standing","Sitting","Walking","Running","Attacking","Casting","Skill","Hit","Dead","Idle","Looting","Pick Up","Riding","Mounted","Falcon","Warg","Dragon","Mado Gear","Frozen","Stoned"]
HAIR = ["Hair Style","Hair Color","Cloth Color","Body Style","Costume"] + [f"Style {i}" for i in range(1, 40)]

def _grf_maps(here):
    """Latin/ascii internal map ids from the GRF mapnametable, if the GRF is present.
    Keeps the expanded map vocabulary even when this script is re-run for a text retrain."""
    mt = os.path.join(here, "..", "..", "GRF", "data", "mapnametable.txt")
    if not os.path.exists(mt):
        return None
    ids = set()
    for ln in open(mt, encoding="latin-1").read().splitlines():
        ln = ln.strip()
        if not ln or ln.startswith("//"):
            continue
        internal = os.path.splitext(ln.split("#")[0].strip())[0]
        if internal and all(ord(c) < 128 for c in internal):
            ids.add(internal)
    return sorted(ids) or None

def _read_lines(path):
    try:
        return [ln.strip() for ln in open(path, encoding="utf-8", errors="replace") if ln.strip()]
    except Exception:
        return []

def _sprite_monster_names(here):
    """Add names implied by the rendered GRF sprite bank.
    These are not always display names, but they teach OCR/selection suggestions the same internal
    monster vocabulary used by icon recognition labels such as mob__poring."""
    raw = os.path.join(here, "icons", "raw")
    manifest = os.path.join(here, "icons", "monster_manifest.json")
    names = set()
    labels = []
    try:
        data = json.load(open(manifest, encoding="utf-8"))
        labels = [c.get("label", "") for c in data.get("classes", [])]
    except Exception:
        labels = [os.path.basename(d) for d in glob.glob(os.path.join(raw, "mob__*"))]
    for label in labels:
        if not label.startswith("mob__"):
            continue
        s = label[5:].strip("_")
        if not s or s.isdigit():
            continue
        clean = re.sub(r"[_\-]+", " ", s).strip()
        clean = re.sub(r"\s+", " ", clean)
        if len(clean) >= 2:
            names.add(clean)
            names.add(clean.title())
    return sorted(names)

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--gamedata", required=True); a = ap.parse_args()
    here = os.path.dirname(os.path.abspath(__file__))
    d = json.load(open(a.gamedata, encoding="utf-8"))
    C = os.path.join(here, "corpus"); os.makedirs(C, exist_ok=True)
    monster_names = [m["name"] for m in d["mobs"]]
    monster_names += _read_lines(os.path.join(C, "rathena_monsters.txt"))
    monster_names += _sprite_monster_names(here)
    def W(name, items):
        items = sorted({x.strip() for x in items if x and x.strip()})
        open(os.path.join(C, name), "w", encoding="utf-8").write("\n".join(items) + "\n"); return len(items)
    out = dict(
        monsters=W("monsters.txt", monster_names),
        items=W("items.txt", [i["name"] for i in d["items"]]),
        skills=W("skills.txt", [s["name"] for s in d["skills"]]),
        maps=W("maps.txt", _grf_maps(os.path.dirname(os.path.abspath(__file__)))
                            or d.get("maps") or ["prontera","payon","geffen","morocc","aldebaran","izlude"]),
        classes=W("classes.txt", CLASSES),
        hud=W("hud.txt", HUD),
        movements=W("movements.txt", MOVES),
        hairstyles=W("hairstyles.txt", HAIR),
    )
    print("corpus:", out)

if __name__ == "__main__":
    main()
