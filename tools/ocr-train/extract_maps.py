#!/usr/bin/env python3
"""extract_maps.py -- pull GRF pre-rendered minimaps into icons/raw/map__<name>/0.png
so the icon embedder learns to recognize maps visually. Idempotent (skips if already done)."""
import os, glob
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
GRF = os.path.join(HERE, "..", "..", "GRF")
MM = os.path.join(GRF, "data", "texture", "유저인터페이스", "map")  # 유저인터페이스/map
RAW = os.path.join(HERE, "icons", "raw")

def main():
    if not os.path.isdir(MM):
        print("[maps] GRF minimap folder not found -> skip (%s)" % MM); return
    existing = glob.glob(os.path.join(RAW, "map__*"))
    bmps = glob.glob(os.path.join(MM, "*.bmp"))
    if existing and len(existing) >= len(bmps) > 0:
        print("[maps] %d map classes already present -> skip" % len(existing)); return
    n = 0
    for bmp in bmps:
        name = os.path.splitext(os.path.basename(bmp))[0]
        d = os.path.join(RAW, "map__" + name); os.makedirs(d, exist_ok=True)
        try:
            Image.open(bmp).convert("RGBA").save(os.path.join(d, "0.png")); n += 1
        except Exception as e:
            print("  skip", name, e)
    print("[maps] added %d map minimap classes -> icons/raw/map__*" % n)

if __name__ == "__main__":
    main()
