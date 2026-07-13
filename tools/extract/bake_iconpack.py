#!/usr/bin/env python3
"""
bake_iconpack.py — Bake GRF item + skill sprites into Assets/iconpack.zip (offline icons, no GRF shipped).

The GRF is NOT included in the release; this bakes only the .bmp sprites we need into a zip the app
embeds (`avares://4rVivi/Assets/iconpack.zip`). Entries:
  items/<itemId>.png    (resolved via idnum2itemresnametable: id -> resname -> <resname>.bmp)
  skills/<aegis>.png    (skill icons are named by aegis in 유저인터페이스/item, e.g. sm_bash.bmp)

Magenta (FF00FF) is the RO transparency key -> made transparent.

Usage:
    python tools/extract/bake_iconpack.py \
        --kro "GRF/kRO Data/data/texture/유저인터페이스" \
        --custom "GRF/data/texture/유저인터페이스" \
        --res src/4rVivi.Core/Data/idnum2itemresnametable.txt \
        --gamedata src/4rVivi.Core/Data/gamedata.json \
        --out src/4rVivi.App/Assets/iconpack.zip
Requires: pillow
"""
import argparse, io, json, os, zipfile
from PIL import Image

def to_png(path):
    im = Image.open(path).convert("RGBA"); px = im.load(); w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if r > 240 and b > 240 and g < 40:   # magenta key
                px[x, y] = (0, 0, 0, 0)
    o = io.BytesIO(); im.save(o, "PNG"); return o.getvalue()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kro", required=True);    ap.add_argument("--custom", default="")
    ap.add_argument("--res", required=True);    ap.add_argument("--gamedata", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    # id -> resname (file is UTF-8)
    enc = "utf-8"
    try: open(a.res, encoding="utf-8").read()
    except: enc = "cp949"
    id2res = {}
    for line in open(a.res, encoding=enc, errors="ignore"):
        if "#" in line:
            p = line.split("#")
            if len(p) > 1 and p[0].strip().isdigit() and p[1]: id2res[int(p[0])] = p[1]

    aegis = sorted({s.get("aegis", "").lower() for s in json.load(open(a.gamedata, encoding="utf-8"))["skills"] if s.get("aegis")})
    bases = [b for b in (a.kro, a.custom) if b]

    def find_item(res):
        for base in bases:
            for sub in ("item", "collection"):
                p = os.path.join(base, sub, res + ".bmp")
                if os.path.exists(p): return p
    def find_skill(ae):
        for base in bases:
            p = os.path.join(base, "item", ae + ".bmp")
            if os.path.exists(p): return p

    items = skills = 0
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for iid, res in id2res.items():
            p = find_item(res)
            if p:
                try: z.writestr(f"items/{iid}.png", to_png(p)); items += 1
                except Exception: pass
        for ae in aegis:
            p = find_skill(ae)
            if p:
                try: z.writestr(f"skills/{ae}.png", to_png(p)); skills += 1
                except Exception: pass
    open(a.out, "wb").write(buf.getvalue())
    print(f"baked items={items} skills={skills} -> {a.out} ({round(os.path.getsize(a.out)/1024)} KB)")

if __name__ == "__main__":
    main()
