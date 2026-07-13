#!/usr/bin/env python3
"""
Build mobid_sprite_map.json from Ragnarok client Lua/LUB data.

Reads:
  - npcidentity.lua/lub: jobtbl.JT_* -> numeric ids
  - jobname.lua/lub: JobNameTable[jobtbl.JT_*] -> sprite token

Writes the map consumed by build_vision_grf.py:
  { "1002": "data\\sprite\\몬스터\\Poring.spr", ... }
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

_ROOT = Path(__file__).resolve().parent.parent.parent
_HERE = Path(__file__).resolve().parent
_MONSTER_DIR_NAME = "\ubaac\uc2a4\ud130"

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def read_text(path: Path) -> str:
    for enc in ("utf-8-sig", "cp949", "latin-1"):
        try:
            return path.read_text(encoding=enc)
        except UnicodeDecodeError:
            continue
    return path.read_text(errors="replace")


def load_mob_ids() -> set[int]:
    gd = json.loads((_ROOT / "src/4rVivi.Core/Data/gamedata.json").read_text(encoding="utf-8"))
    out: set[int] = set()
    for m in gd.get("mobs", []):
        try:
            out.add(int(m["id"]))
        except (KeyError, TypeError, ValueError):
            pass
    return out


def find_data_files(client_root: Path, names: tuple[str, ...]) -> list[Path]:
    roots = [
        client_root / "data/luafiles514/lua files/datainfo",
        client_root / "data/lua files/datainfo",
        _ROOT / "GRF/data/luafiles514/lua files/datainfo",
        _ROOT / "GRF/data/lua files/datainfo",
    ]
    found: list[Path] = []
    for root in roots:
        for name in names:
            p = root / name
            if p.exists():
                found.append(p)
    for name in names:
        found.extend(client_root.rglob(name))
    out: list[Path] = []
    seen = set()
    for p in found:
        key = str(p).lower()
        if key not in seen:
            seen.add(key)
            out.append(p)
    return out


def parse_identity(path: Path) -> dict[str, int]:
    text = read_text(path)
    out: dict[str, int] = {}
    for m in re.finditer(r"\b(JT_[A-Za-z0-9_]+)\s*=\s*(-?\d+)\s*,?", text):
        out[m.group(1)] = int(m.group(2))
    return out


def parse_job_names(path: Path) -> dict[str, str]:
    text = read_text(path)
    out: dict[str, str] = {}
    pat = re.compile(r"\[\s*jobtbl\.(JT_[A-Za-z0-9_]+)\s*\]\s*=\s*['\"]([^'\"]+)['\"]")
    for m in pat.finditer(text):
        out[m.group(1)] = m.group(2).strip()
    return out


def find_monster_sprite_dir(client_root: Path, explicit: Path | None = None) -> Path | None:
    if explicit is not None:
        return explicit if explicit.exists() else None

    candidates = [
        client_root / "data" / "sprite" / _MONSTER_DIR_NAME,
        _ROOT / "GRF" / "data" / "sprite" / _MONSTER_DIR_NAME,
    ]
    for p in candidates:
        if p.exists():
            return p

    sprite_root = client_root / "data" / "sprite"
    if not sprite_root.exists():
        sprite_root = _ROOT / "GRF" / "data" / "sprite"
    if not sprite_root.exists():
        return None

    dirs = [p for p in sprite_root.iterdir() if p.is_dir()]
    if not dirs:
        return None
    return max(dirs, key=lambda p: len(list(p.glob("*.spr"))))


def resolve_sprite_name(sprite_dir: Path | None, token: str) -> str:
    name = token.replace("/", "\\")
    if not name.lower().endswith(".spr"):
        name += ".spr"
    if sprite_dir is None:
        return name

    candidates = {
        name,
        name.lower(),
        name.upper(),
        Path(name).name,
        Path(name).name.lower(),
        Path(name).name.upper(),
    }
    existing = {p.name.lower(): p.name for p in sprite_dir.glob("*.spr")}
    for cand in candidates:
        hit = existing.get(Path(cand).name.lower())
        if hit:
            return hit
    return Path(name).name


def build_sprite_map(
    client_root: Path,
    out_path: Path,
    npcidentity: Path | None = None,
    jobname: Path | None = None,
    monster_dir: Path | None = None,
) -> dict[int, str]:
    npc_candidates = [npcidentity] if npcidentity else find_data_files(client_root, ("npcidentity.lua", "npcidentity.lub"))
    job_candidates = [jobname] if jobname else find_data_files(client_root, ("jobname.lua", "jobname.lub"))
    npcidentity = next((p for p in npc_candidates if p and len(parse_identity(p)) > 0), None)
    jobname = next((p for p in job_candidates if p and len(parse_job_names(p)) > 0), None)
    if npcidentity is None or jobname is None:
        raise SystemExit("[sprite-map] missing npcidentity/jobname Lua files")

    ids = parse_identity(npcidentity)
    names = parse_job_names(jobname)
    if len(ids) < 100 or len(names) < 100:
        raise SystemExit(f"[sprite-map] parsed too few rows: ids={len(ids)} names={len(names)}")
    mob_ids = load_mob_ids()
    sprite_dir = find_monster_sprite_dir(client_root, monster_dir)
    monster_dir_name = sprite_dir.name if sprite_dir is not None else _MONSTER_DIR_NAME

    out: dict[int, str] = {}
    verified = 0
    for jt, mob_id in ids.items():
        if mob_id not in mob_ids:
            continue
        token = names.get(jt)
        if not token:
            continue
        if token.lower().endswith(".gr2"):
            continue
        sprite_name = resolve_sprite_name(sprite_dir, token)
        if sprite_dir is not None and (sprite_dir / sprite_name).exists():
            verified += 1
        out[mob_id] = f"data\\sprite\\{monster_dir_name}\\{sprite_name}"

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(
        json.dumps({str(k): out[k] for k in sorted(out)}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(f"[sprite-map] identity={npcidentity}")
    print(f"[sprite-map] jobname={jobname}")
    print(f"[sprite-map] monster_dir={sprite_dir or '(not found; wrote expected paths)'}")
    print(f"[sprite-map] wrote {len(out)} mobs to {out_path} verified_files={verified}")
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description="Build tools/vision-grf/mobid_sprite_map.json from client Lua data.")
    ap.add_argument("--client", type=Path, default=_ROOT, help="RO client root or extracted GRF root.")
    ap.add_argument("--npcidentity", type=Path)
    ap.add_argument("--jobname", type=Path)
    ap.add_argument("--monster-dir", type=Path, help="Extracted data/sprite/monster folder, optional.")
    ap.add_argument("--out", type=Path, default=_HERE / "mobid_sprite_map.json")
    args = ap.parse_args()
    build_sprite_map(args.client, args.out, args.npcidentity, args.jobname, args.monster_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
