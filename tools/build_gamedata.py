#!/usr/bin/env python3
"""
build_gamedata.py — Generate the calculator's gamedata.json from rAthena's db/re YAMLs.

Why: the bundled gamedata.json had an empty `equips` array and mobs with no DEF/stats,
so per-slot equipment dropdowns and enemy auto-fill couldn't work. This script reads the
authoritative rAthena Renewal databases and emits a categorized JSON the app understands.

Usage:
    # 1) get rAthena (once)
    git clone --depth 1 https://github.com/rathena/rathena.git
    # 2) run this, pointing at the db/re folder
    python tools/build_gamedata.py --re ./rathena/db/re --out ./src/4rVivi.App/gamedata.json
    #    (also copy gamedata.json next to the built exe, or wherever the app loads it)

Requires: pyyaml  (pip install pyyaml)

Sources (rAthena master, db/re):
    item_db_equip.yml, item_db_etc.yml (cards), item_db_usable.yml,
    item_randomopt_db.yml (enchants/random options), item_enchant.yml,
    mob_db.yml, attr_fix.yml, elemental_db.yml, job_aspd.yml, job_basepoints.yml
"""
import argparse, json, os, sys

try:
    import yaml
except ImportError:
    sys.exit("Missing pyyaml. Run: pip install pyyaml")

# ---- rAthena Location key -> our slot tag (matches EquipInfo.Loc usage in the app) ----
LOC_KEYS = [
    "Head_Top", "Head_Mid", "Head_Low",
    "Armor", "Right_Hand", "Left_Hand", "Both_Hand",
    "Garment", "Shoes", "Right_Accessory", "Left_Accessory",
    "Costume_Head_Top", "Costume_Head_Mid", "Costume_Head_Low", "Costume_Garment",
    "Ammo", "Shadow_Armor", "Shadow_Weapon", "Shadow_Shield",
    "Shadow_Shoes", "Shadow_Right_Accessory", "Shadow_Left_Accessory",
]

def load_yaml(path):
    if not os.path.isfile(path):
        print(f"  ! skip (not found): {path}")
        return []
    with open(path, "r", encoding="utf-8") as f:
        doc = yaml.safe_load(f) or {}
    body = doc.get("Body", [])
    print(f"  + {os.path.basename(path)}: {len(body)} entries")
    return body

def truthy_keys(d):
    """rAthena uses maps like {Right_Hand: true}. Return the keys that are true."""
    if not isinstance(d, dict):
        return []
    return [k for k, v in d.items() if v]

def parse_equips(body):
    out = []
    for e in body:
        locs = truthy_keys(e.get("Locations", {}))
        jobs = truthy_keys(e.get("Jobs", {})) or truthy_keys(e.get("Classes", {}))
        out.append({
            "id": e.get("Id", 0),
            "aegis": e.get("AegisName", ""),
            "name": e.get("Name", ""),
            "type": e.get("Type", ""),
            "subtype": e.get("SubType", ""),
            "loc": locs,
            "jobs": jobs,
            "wlvl": e.get("WeaponLevel", 0),
            "atk": e.get("Attack", 0),
            "matk": e.get("MagicAttack", 0),
            "def": e.get("Defense", 0),
            "slots": e.get("Slots", 0),
            "equipLvl": e.get("EquipLevelMin", 0),
            "refineable": bool(e.get("Refineable", False)),
            "script": (e.get("Script", "") or "").strip(),
            "bonuses": {},  # the app can keep parsing scripts later; kept for schema compat
        })
    return out

def parse_cards(body):
    out = []
    for e in body:
        if str(e.get("Type", "")).lower() != "card":
            continue
        out.append({
            "id": e.get("Id", 0),
            "aegis": e.get("AegisName", ""),
            "name": e.get("Name", ""),
            "loc": truthy_keys(e.get("Locations", {})),
            "script": (e.get("Script", "") or "").strip(),
        })
    return out

def parse_items(*bodies):
    out = []
    for body in bodies:
        for e in body:
            out.append({
                "id": e.get("Id", 0),
                "aegis": e.get("AegisName", ""),
                "name": e.get("Name", ""),
                "type": e.get("Type", ""),
                "slots": e.get("Slots", 0),
                "weight": e.get("Weight", 0),
                "script": (e.get("Script", "") or "").strip(),
            })
    return out

def parse_enchants(randopt_body):
    out = []
    for e in randopt_body:
        out.append({
            "id": e.get("Id", 0),
            "name": e.get("Option", e.get("Name", "")),
            "script": (e.get("Script", "") or "").strip(),
        })
    return out

def parse_mobs(body):
    out = []
    for m in body:
        modes = m.get("Modes", {}) or {}
        is_mvp = bool(modes.get("Mvp", False)) or bool(m.get("Mvp", False))
        out.append({
            "id": m.get("Id", 0),
            "aegis": m.get("AegisName", ""),
            "name": m.get("Name", ""),
            "level": m.get("Level", 1),
            "hp": m.get("Hp", 1),
            "atk": m.get("Attack", 0),
            "matk": m.get("Attack2", 0),
            "def": m.get("Defense", 0),
            "mdef": m.get("MagicDefense", 0),
            "str": m.get("Str", 1),
            "agi": m.get("Agi", 1),
            "vit": m.get("Vit", 1),
            "int": m.get("Int", 1),
            "dex": m.get("Dex", 1),
            "luk": m.get("Luk", 1),
            "race": m.get("Race", ""),
            "element": m.get("Element", ""),
            "elementLevel": m.get("ElementLevel", 1),
            "size": m.get("Size", ""),
            "baseExp": m.get("BaseExp", 0),
            "jobExp": m.get("JobExp", 0),
            "mvp": is_mvp,
            "drops": [{"item": d.get("Item", ""), "rate": d.get("Rate", 0)}
                      for d in (m.get("Drops", []) or [])],
        })
    return out

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--re", required=True, help="path to rathena/db/re")
    ap.add_argument("--out", required=True, help="output gamedata.json path")
    a = ap.parse_args()
    re = a.re

    print("Reading rAthena db/re YAMLs…")
    equip_body  = load_yaml(os.path.join(re, "item_db_equip.yml"))
    etc_body    = load_yaml(os.path.join(re, "item_db_etc.yml"))
    usable_body = load_yaml(os.path.join(re, "item_db_usable.yml"))
    randopt     = load_yaml(os.path.join(re, "item_randomopt_db.yml"))
    mob_body    = load_yaml(os.path.join(re, "mob_db.yml"))

    # Preserve skills/maps from an existing gamedata.json (db/re has no skill list here).
    prev_skills, prev_maps = [], []
    if os.path.isfile(a.out):
        try:
            old = json.load(open(a.out, encoding="utf-8"))
            prev_skills, prev_maps = old.get("skills", []), old.get("maps", [])
            print(f"  (preserving {len(prev_skills)} skills, {len(prev_maps)} maps from existing file)")
        except Exception:
            pass

    # Optional: parse skill_db.yml if present in db/re (it usually is on master).
    skill_body = load_yaml(os.path.join(re, "skill_db.yml"))
    # Description = human-readable ("Bash"); Name = AegisName ("SM_BASH"). Prefer Description.
    skills = [{"id": s.get("Id", 0),
               "name": s.get("Description") or s.get("Name", ""),
               "castMs": 0, "delayMs": 0, "cooldownMs": 0} for s in skill_body] or prev_skills

    data = {
        "equips": parse_equips(equip_body),
        "cards": parse_cards(etc_body),
        "items": parse_items(equip_body, etc_body, usable_body),  # full pool for search
        "enchants": parse_enchants(randopt),
        "mobs": parse_mobs(mob_body),
        "skills": skills,
        "maps": prev_maps,
    }

    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))

    print("\nWrote", a.out)
    print(f"  equips={len(data['equips'])} cards={len(data['cards'])} "
          f"items={len(data['items'])} enchants={len(data['enchants'])} mobs={len(data['mobs'])}")
    print("Copy gamedata.json to where the app loads it (beside the exe / embedded resource).")

if __name__ == "__main__":
    main()
