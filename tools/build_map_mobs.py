#!/usr/bin/env python3
"""
Generate src/4rVivi.Core/Data/map_mobs.json from rAthena monster spawn scripts.

The source of truth is rAthena's npc/re/mobs tree. The generated file is used by
Smart Bot vision focus so monster names are narrowed by the selected map.
"""
from __future__ import annotations

import argparse
import json
import re
import zipfile
from collections import defaultdict
from pathlib import Path

# Repo root, resolved from THIS file's location so the script works from any cwd.
# build_map_mobs.py lives in <repo>/tools/, so root = parent of parent.
_ROOT = Path(__file__).resolve().parent.parent


SPAWN_RE = re.compile(
    r"^\s*([^\t/][^\t]*)\t(?:boss_)?monster\t([^\t]+)\t([^,\s]+)\s*,\s*(-?\d+)"
)


def key(value: str | None) -> str:
    if not value:
        return ""
    return "".join(ch.lower() for ch in value if ch.isalnum())


def load_mobs(gamedata_path: Path) -> tuple[dict[str, dict], dict[str, dict]]:
    data = json.loads(gamedata_path.read_text(encoding="utf-8"))
    by_id: dict[str, dict] = {}
    by_key: dict[str, dict] = {}
    for mob in data.get("mobs", []):
        mob_id = str(mob.get("id", "")).strip()
        if mob_id:
            by_id[mob_id] = mob
        for alias in (mob.get("aegis", ""), mob.get("name", ""), mob_id):
            k = key(alias)
            if k and k not in by_key:
                by_key[k] = mob
        aegis = str(mob.get("aegis", ""))
        if aegis.startswith("G_"):
            by_key.setdefault(key(aegis[2:]), mob)
    return by_id, by_key


def spawn_subdirs(mode: str) -> list[Path]:
    paths = [Path("npc/mobs")]
    if mode in ("re", "both"):
        paths.append(Path("npc/re/mobs"))
    if mode in ("pre-re", "both"):
        paths.append(Path("npc/pre-re/mobs"))
    return paths


def iter_spawn_files(source_path: Path, mode: str):
    if source_path.is_dir():
        for subdir in spawn_subdirs(mode):
            root = source_path / subdir
            if not root.exists():
                continue
            for path in sorted(root.rglob("*.txt")):
                text = path.read_text(encoding="utf-8", errors="replace")
                yield str(path.relative_to(source_path)).replace("\\", "/"), text.splitlines()
        return

    with zipfile.ZipFile(source_path) as archive:
        wanted_suffixes = tuple(str(path).replace("\\", "/") + "/" for path in spawn_subdirs(mode))

        for info in archive.infolist():
            if info.is_dir() or not info.filename.endswith(".txt"):
                continue
            normalized = info.filename.replace("\\", "/")
            parts = normalized.split("/")
            if len(parts) < 3:
                continue
            relative_options = [normalized, "/".join(parts[1:])]
            if not any(relative.startswith(suffix) for relative in relative_options for suffix in wanted_suffixes):
                continue
            with archive.open(info) as stream:
                text = stream.read().decode("utf-8", errors="replace")
            yield normalized, text.splitlines()


def resolve_mob(token: str, display_name: str, by_id: dict[str, dict], by_key: dict[str, dict]) -> tuple[str, str]:
    mob = by_id.get(token.strip()) or by_key.get(key(token)) or by_key.get(key(display_name))
    if mob:
        return str(mob.get("aegis") or display_name), str(mob.get("name") or display_name)
    return token if not token.isdigit() else display_name, display_name


def build(source_path: Path, gamedata_path: Path, mode: str) -> dict[str, list[dict]]:
    by_id, by_key = load_mobs(gamedata_path)
    maps: dict[str, dict[str, dict]] = defaultdict(dict)

    for _filename, lines in iter_spawn_files(source_path, mode):
        for raw in lines:
            line = raw.strip()
            if not line or line.startswith("//"):
                continue
            match = SPAWN_RE.match(line)
            if not match:
                continue

            loc, display_name, mob_token, amount_text = match.groups()
            map_name = loc.split(",", 1)[0].strip()
            if not map_name:
                continue
            try:
                amount = int(amount_text)
            except ValueError:
                amount = 1
            if amount <= 0:
                amount = 1

            aegis, name = resolve_mob(mob_token, display_name.strip(), by_id, by_key)
            mob_key = key(aegis) or key(name)
            if not mob_key:
                continue
            row = maps[map_name].setdefault(mob_key, {"aegis": aegis, "amount": 0, "name": name})
            row["amount"] += amount
            if not row.get("name"):
                row["name"] = name

    return {
        map_name: sorted(rows.values(), key=lambda r: (-int(r["amount"]), str(r["name"]).lower()))
        for map_name, rows in sorted(maps.items(), key=lambda kv: kv[0].lower())
        if rows
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, help="Path to a rAthena checkout directory or rathena-master zip.")
    ap.add_argument("--zip", dest="zip_path", type=Path, help="Deprecated alias for --source.")
    ap.add_argument("--gamedata", default=_ROOT / "src/4rVivi.Core/Data/gamedata.json", type=Path)
    ap.add_argument("--out", default=_ROOT / "src/4rVivi.Core/Data/map_mobs.json", type=Path)
    ap.add_argument("--mode", choices=("re", "pre-re", "both"), default="re")
    args = ap.parse_args()

    source = args.source or args.zip_path
    if source is None:
        ap.error("--source is required")

    result = build(source, args.gamedata, args.mode)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    spawn_count = sum(len(v) for v in result.values())
    print(f"Wrote {args.out} with {len(result)} maps and {spawn_count} map-monster rows from {source} ({args.mode}).")
    if "pay_dun00" in result:
        print("pay_dun00:", ", ".join(row["name"] for row in result["pay_dun00"]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
