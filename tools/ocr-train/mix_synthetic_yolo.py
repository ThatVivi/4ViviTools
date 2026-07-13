#!/usr/bin/env python3
"""
mix_synthetic_yolo.py -- add generated GRF monster scenes into yolo_real.

The real paid datasets are still the base. Synthetic scenes add coverage for monster poses,
directions, occlusion, and map-scroll backgrounds. Both datasets use the same class ids:
0 monster 1 player 2 loot 3 portal 4 target 5 target_hp 6 player_hp.
"""
import glob
import os
import shutil
import argparse

HERE = os.path.dirname(os.path.abspath(__file__))
SYN = os.path.join(HERE, "yolo")
REAL = os.path.join(HERE, "yolo_real")


def copy_split(split):
    img_src = os.path.join(SYN, "images", split)
    lab_src = os.path.join(SYN, "labels", split)
    img_dst = os.path.join(REAL, "images", split)
    lab_dst = os.path.join(REAL, "labels", split)
    os.makedirs(img_dst, exist_ok=True)
    os.makedirs(lab_dst, exist_ok=True)

    copied = 0
    for img in glob.glob(os.path.join(img_src, "*.*")):
        stem, ext = os.path.splitext(os.path.basename(img))
        lab = os.path.join(lab_src, stem + ".txt")
        if not os.path.exists(lab):
            continue
        out_stem = "synthetic_grf__" + stem
        shutil.copy2(img, os.path.join(img_dst, out_stem + ext.lower()))
        shutil.copy2(lab, os.path.join(lab_dst, out_stem + ".txt"))
        copied += 1
    return copied


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--recipe", default="", help="recipe/version marker for resumable runners")
    ap.parse_args()
    if not os.path.exists(os.path.join(SYN, "data.yaml")):
        print("[mix-yolo] no synthetic yolo/data.yaml -> run gen_yolo_scenes.py first")
        return
    train = copy_split("train")
    val = copy_split("val")
    print("[mix-yolo] copied synthetic scenes into yolo_real: train=%d val=%d" % (train, val))


if __name__ == "__main__":
    main()
