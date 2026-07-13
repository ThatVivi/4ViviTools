#!/usr/bin/env python3
"""
merge_datasets.py -- unify the paid Roboflow YOLOv8 datasets in TrainingData/ into ONE multi-class
detection set for an auto-attack bot. Handles inconsistent class names across datasets, OBB->AABB,
and the roboflow train/valid/test layout. Output: yolo_real/{images,labels}/{train,val} + data.yaml.

Unified classes:
  0 monster   1 player   2 loot   3 portal   4 target   5 target_hp   6 player_hp
Boxes whose source label maps to None (cursor/ui junk) are dropped.
"""
import argparse, os, glob, shutil, re, sys, subprocess, random
try:
    import yaml
except Exception:
    subprocess.call([sys.executable, "-m", "pip", "install", "--quiet", "--break-system-packages", "pyyaml"])
    try:
        import yaml
    except Exception:
        yaml = None

HERE = os.path.dirname(os.path.abspath(__file__))
SRC  = os.path.join(HERE, "TrainingData")
OUT  = os.path.join(HERE, "yolo_real")

CLASSES = ["monster", "player", "loot", "portal", "target", "target_hp", "player_hp"]
CID = {c: i for i, c in enumerate(CLASSES)}

HARD_NEGATIVE_HINTS = (
    "screenshot", "screen", "capture", "negative", "background", "town", "ui", "chat",
    "damage", "number", "text", "empty"
)


def unify(name):
    n = name.strip().lower().replace(" ", "_")
    # HUD / targeting (auto-attack set)
    if n in ("target_hp", "targethp", "enemy_hp"): return "target_hp"
    if n in ("player_hp", "playerhp", "hp", "my_hp"): return "player_hp"
    if n in ("target",): return "target"
    # loot / drops / pickables
    if any(k in n for k in ("loot", "drop", "item", "herb", "straw", "bottle", "card", "rare")): return "loot"
    # portals / warps
    if any(k in n for k in ("portal", "warp")): return "portal"
    # players (self + others)
    if n in ("player", "self", "main", "human", "char", "selfpersone") or "player" in n or n in ("other_player",): return "player"
    # explicit junk -> drop
    if n in ("cursor", "ignorar", "ignore", "cancel", "shop", "self_hp", "ui", "text", "damage", "number"): return None
    # dead corpses -> drop (bot shouldn't attack them)
    if n.startswith("dead") or "dead_" in n: return None
    # everything else is treated as a monster (these are monster-detection datasets)
    return "monster"

def to_aabb(parts):
    """Return (cx,cy,w,h) normalized from a YOLO line's coords. Handles std (4) and OBB (8)."""
    vals = [float(x) for x in parts]
    if len(vals) == 4:
        return vals
    if len(vals) >= 8:  # OBB: x1 y1 x2 y2 x3 y3 x4 y4 -> bounding box
        xs = vals[0:8:2]; ys = vals[1:8:2]
        x0, x1 = min(xs), max(xs); y0, y1 = min(ys), max(ys)
        return [(x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0]
    return None

def load_names(ds):
    for cand in ("data.yaml", "data.yml"):
        p = os.path.join(ds, cand)
        if not os.path.exists(p): continue
        txt = open(p, encoding="utf-8", errors="replace").read()
        if yaml is not None:
            try:
                d = yaml.safe_load(txt)
                names = d.get("names")
                if isinstance(names, dict): names = [names[k] for k in sorted(names)]
                if names: return list(names)
            except Exception:
                pass
        # regex fallback: names: ['a','b',...]  or  names:\n  - a\n  - b
        m = re.search(r"names\s*:\s*\[([^\]]*)\]", txt)
        if m:
            return [x.strip().strip("'\"") for x in m.group(1).split(",") if x.strip()]
        block = re.findall(r"^\s*-\s*(.+)$", txt[txt.find("names"):], re.M) if "names" in txt else []
        if block:
            return [b.strip().strip("'\"") for b in block]
    return []

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--reset", action="store_true", help="delete yolo_real before rebuilding")
    ap.add_argument("--empty-negatives", type=float, default=0.10,
                    help="keep this fraction of unlabeled/empty screenshots as hard negatives")
    ap.add_argument("--include-unapproved-video", action="store_true",
                    help="include Video_*.yolov8 pseudo-label datasets even without approved_for_training.json")
    ap.add_argument("--recipe", default="", help="recipe/version marker for resumable runners")
    ap.add_argument("--seed", type=int, default=1337)
    a = ap.parse_args()
    random.seed(a.seed)

    if a.reset and os.path.isdir(OUT):
        shutil.rmtree(OUT)
    for sub in ("images/train", "images/val", "labels/train", "labels/val"):
        os.makedirs(os.path.join(OUT, sub), exist_ok=True)
    datasets = sorted(d for d in glob.glob(os.path.join(SRC, "*")) if os.path.isdir(d))
    stats = {c: 0 for c in CLASSES}; dropped = 0; empty_kept = 0; imgs = {"train": 0, "val": 0}
    skipped_video = []
    for ds in datasets:
        base_name = os.path.basename(ds)
        if base_name.lower().startswith("video_") and not a.include_unapproved_video:
            marker = os.path.join(ds, "approved_for_training.json")
            if not os.path.exists(marker):
                skipped_video.append(base_name)
                continue
        names = load_names(ds)
        ds_name = os.path.basename(ds).lower()
        allow_empty_from_name = any(h in ds_name for h in HARD_NEGATIVE_HINTS)
        if not names:
            print("[info] no class names, using as possible hard negatives:", os.path.basename(ds))
        for split_src, split_dst in (("train", "train"), ("valid", "val"), ("test", "train")):
            idir = os.path.join(ds, split_src, "images")
            ldir = os.path.join(ds, split_src, "labels")
            if not os.path.isdir(idir): continue
            tag = re.sub(r"[^a-zA-Z0-9]+", "_", os.path.basename(ds))[:40]
            for img in glob.glob(os.path.join(idir, "*")):
                base = os.path.splitext(os.path.basename(img))[0]
                lab = os.path.join(ldir, base + ".txt")
                out_lines = []
                if names and os.path.exists(lab):
                    for line in open(lab, encoding="utf-8"):
                        p = line.split()
                        if len(p) < 5: continue
                        try: ci = int(p[0])
                        except ValueError: continue
                        if ci < 0 or ci >= len(names): continue
                        u = unify(str(names[ci]))
                        if u is None: dropped += 1; continue
                        box = to_aabb(p[1:])
                        if not box: continue
                        cx, cy, w, h = box
                        if w <= 0 or h <= 0: continue
                        out_lines.append("%d %.6f %.6f %.6f %.6f" % (CID[u], cx, cy, w, h))
                        stats[u] += 1
                # Keep a controlled slice of empty/hard-negative images. These teach YOLO that RO
                # damage numbers, names, UI, town crowds, and plain map texture are not monsters.
                if not out_lines:
                    keep_empty = allow_empty_from_name or random.random() < max(0.0, min(1.0, a.empty_negatives))
                    if not keep_empty:
                        continue
                    empty_kept += 1
                newbase = "%s__%s" % (tag, base)
                shutil.copy(img, os.path.join(OUT, "images", split_dst, newbase + os.path.splitext(img)[1]))
                open(os.path.join(OUT, "labels", split_dst, newbase + ".txt"), "w").write("\n".join(out_lines) + "\n")
                imgs[split_dst] += 1
    data = {"path": OUT, "train": "images/train", "val": "images/val",
            "nc": len(CLASSES), "names": CLASSES}
    with open(os.path.join(OUT, "data.yaml"), "w") as fy:
        fy.write("path: %s\ntrain: images/train\nval: images/val\nnc: %d\nnames: [%s]\n"
                 % (OUT.replace("\\", "/"), len(CLASSES), ", ".join("'%s'" % c for c in CLASSES)))
    print("=== merged ===")
    print("images: train=%d val=%d   dropped boxes=%d   hard-negative images=%d" % (imgs["train"], imgs["val"], dropped, empty_kept))
    for c in CLASSES: print("  %-10s %d boxes" % (c, stats[c]))
    if skipped_video:
        print("\n[review] skipped unapproved video pseudo-label datasets:")
        for name in skipped_video:
            print("  - %s (create approved_for_training.json after reviewing labels)" % name)
    print("data.yaml ->", os.path.join(OUT, "data.yaml"))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc(); sys.exit(1)
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
