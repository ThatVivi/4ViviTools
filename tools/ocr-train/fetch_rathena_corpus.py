#!/usr/bin/env python3
"""
Fetch OCR vocabulary from upstream rAthena databases.

Outputs go to tools/ocr-train/corpus/:
  rathena_monsters.txt
  rathena_items.txt
  rathena_skills.txt
  rathena_maps.txt
  rathena_status.txt
  rathena_jobs.txt
  rathena_ocr_words.txt
  rathena_sources.json

This is intentionally separate from build_corpus.py: the app's bundled gamedata.json remains the
shipping dictionary, while these files are fresh upstream training/research corpora for OCR
fine-tuning, dictionary expansion, and hard-example review.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
from typing import Any, Iterable


HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")
BASE = "https://raw.githubusercontent.com/rathena/rathena/master"

YAML_SOURCES = {
    "monsters": [f"{BASE}/db/re/mob_db.yml"],
    "items": [
        f"{BASE}/db/re/item_db.yml",
        f"{BASE}/db/re/item_db_equip.yml",
        f"{BASE}/db/re/item_db_etc.yml",
        f"{BASE}/db/re/item_db_usable.yml",
    ],
    "skills": [f"{BASE}/db/re/skill_db.yml"],
    "status": [f"{BASE}/db/re/status.yml"],
    "jobs": [
        f"{BASE}/db/re/job_stats.yml",
        f"{BASE}/db/re/job_basepoints.yml",
        f"{BASE}/db/re/job_aspd.yml",
    ],
}

TEXT_SOURCES = {
    "maps": [f"{BASE}/db/map_index.txt"],
}

HUD_WORDS = {
    "HP", "SP", "AP", "EXP", "Base", "Job", "Weight", "Zeny", "Map", "Party", "Guild",
    "Skill", "Inventory", "Storage", "Cart", "Equipment", "Status", "Quest", "Mail",
    "Trade", "Shop", "Vending", "Whisper", "Cast", "Delay", "Cooldown", "Poison",
    "Curse", "Stun", "Freeze", "Frozen", "Sleep", "Blind", "Silence", "Stone",
    "Bleeding", "Confusion", "Dead", "Sitting", "Standing", "Walking", "Attacking",
}


def ensure_yaml():
    try:
        import yaml  # type: ignore
        return yaml
    except Exception:
        subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", "pyyaml"])
        import yaml  # type: ignore
        return yaml


def fetch(url: str, timeout: int = 60) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "4ViviTools-OCR-corpus-fetcher"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read().decode("utf-8", errors="replace")


def walk(obj: Any) -> Iterable[Any]:
    if isinstance(obj, dict):
        yield obj
        for value in obj.values():
            yield from walk(value)
    elif isinstance(obj, list):
        for value in obj:
            yield from walk(value)


def clean_name(value: Any) -> str:
    if value is None:
        return ""
    s = str(value).strip()
    s = re.sub(r"\s+", " ", s)
    s = s.strip(" -_\t\r\n")
    if not s:
        return ""
    if s.lower() in {"true", "false", "none", "null"}:
        return ""
    return s


def displayify_identifier(value: str) -> str:
    value = value.strip()
    if not value:
        return ""
    value = value.replace("_", " ").replace("-", " ")
    value = re.sub(r"\s+", " ", value)
    if value.isupper() or value.islower():
        return value.title()
    return value


def names_from_yaml(text: str, category: str) -> set[str]:
    yaml = ensure_yaml()
    try:
        data = yaml.safe_load(text)
    except Exception:
        data = None

    out: set[str] = set()
    keys = {
        "Name", "AegisName", "JapaneseName", "AliasName", "Description", "DescriptionName",
        "DisplayName", "ClientName", "Job", "Skill", "Status", "Icon",
    }

    if data is not None:
        for node in walk(data):
            if not isinstance(node, dict):
                continue
            if category == "jobs" and isinstance(node.get("Jobs"), dict):
                for job_name in node["Jobs"].keys():
                    raw = clean_name(job_name)
                    if raw:
                        out.add(displayify_identifier(raw))
            for key in keys:
                if key not in node:
                    continue
                raw = clean_name(node.get(key))
                if not raw:
                    continue
                if key in {"AegisName", "Job", "Skill", "Status", "Icon"}:
                    out.add(displayify_identifier(raw))
                else:
                    out.add(raw)

    # Fallback regex keeps the script useful if a YAML shape changes.
    for m in re.finditer(r"^\s*(?:Name|AegisName|JapaneseName|DisplayName|ClientName|Job):\s*(.+?)\s*$", text, re.M):
        raw = clean_name(m.group(1).strip("'\""))
        if raw:
            out.add(displayify_identifier(raw) if "_" in raw else raw)

    # Keep only OCR-ish strings, not long item descriptions or scripts.
    max_len = 64 if category in {"monsters", "items", "skills"} else 48
    return {s for s in out if 1 < len(s) <= max_len and not s.startswith("{") and not s.startswith("[")}


def maps_from_text(text: str) -> set[str]:
    out: set[str] = set()
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("//"):
            continue
        name = line.split()[0]
        name = os.path.splitext(name)[0]
        if re.match(r"^[A-Za-z0-9_@-]+$", name):
            out.add(name)
            out.add(displayify_identifier(name))
    return out


def write_list(name: str, values: Iterable[str]) -> int:
    os.makedirs(CORPUS, exist_ok=True)
    vals = sorted({clean_name(v) for v in values if clean_name(v)}, key=str.lower)
    path = os.path.join(CORPUS, name)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(vals))
        f.write("\n")
    return len(vals)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--offline-ok", action="store_true", help="Return success even if a source cannot be downloaded.")
    args = ap.parse_args()

    started = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    source_log: dict[str, Any] = {"fetchedAt": started, "sources": {}, "counts": {}}
    buckets: dict[str, set[str]] = {k: set() for k in list(YAML_SOURCES) + list(TEXT_SOURCES)}

    failures: list[str] = []
    for category, urls in YAML_SOURCES.items():
        source_log["sources"][category] = urls
        for url in urls:
            try:
                text = fetch(url)
                buckets[category].update(names_from_yaml(text, category))
                print(f"[ok] {category}: {url}")
            except Exception as exc:
                failures.append(f"{url}: {exc}")
                print(f"[warn] {url}: {exc}", file=sys.stderr)

    for category, urls in TEXT_SOURCES.items():
        source_log["sources"][category] = urls
        for url in urls:
            try:
                text = fetch(url)
                buckets[category].update(maps_from_text(text))
                print(f"[ok] {category}: {url}")
            except Exception as exc:
                failures.append(f"{url}: {exc}")
                print(f"[warn] {url}: {exc}", file=sys.stderr)

    buckets["hud"] = set(HUD_WORDS)

    merged: set[str] = set()
    for category, values in buckets.items():
        if not values:
            continue
        count = write_list(f"rathena_{category}.txt", values)
        source_log["counts"][category] = count
        merged.update(values)

    source_log["counts"]["merged"] = write_list("rathena_ocr_words.txt", merged)
    source_log["failures"] = failures
    os.makedirs(CORPUS, exist_ok=True)
    with open(os.path.join(CORPUS, "rathena_sources.json"), "w", encoding="utf-8") as f:
        json.dump(source_log, f, indent=2)

    print("counts:", source_log["counts"])
    if failures and not args.offline_ok:
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
