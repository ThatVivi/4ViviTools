#!/usr/bin/env python3
"""
gen_yolo_scenes.py -- self-labeled synthetic training data for an ENTITY detector (YOLO).

We have no annotated screenshots, but we DO have the GRF assets. So we composite real game
sprites (icons/raw/spr_*) onto map backgrounds (icons/raw/map__* minimaps + procedural) at random
positions/scales. The paste rectangle IS the ground-truth box -> free, exact labels.

Single class "entity": YOLO finds WHERE things are; the icon embedder says WHICH (12k classes).
Output (yolo/):  images/{train,val}/*.jpg  labels/{train,val}/*.txt  data.yaml
Idempotent: skips if already generated. Flags: --scenes N(3000) --val 0.1 --imgsz 640 --max-objs 14
"""
import argparse, glob, os, random
from PIL import Image, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "icons", "raw")
OUT = os.path.join(HERE, "yolo")

def _sprites():
    p = glob.glob(os.path.join(RAW, "spr_*", "0.png"))
    return p or glob.glob(os.path.join(RAW, "*", "0.png"))   # fallback: any icon

def _bgs():
    return glob.glob(os.path.join(RAW, "map__*", "0.png"))

def _procedural(sz):
    r = random.random()
    if r < 0.4:
        return Image.new("RGB", (sz, sz), tuple(random.randint(0, 90) for _ in range(3)))
    im = Image.new("RGB", (sz, sz)); px = im.load()
    a = tuple(random.randint(0, 120) for _ in range(3)); b = tuple(random.randint(0, 120) for _ in range(3))
    for y in range(sz):
        t = y / sz
        col = tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))
        for x in range(sz): px[x, y] = col
    return im

def _bg(sz, bgs):
    if bgs and random.random() < 0.6:
        try:
            im = Image.open(random.choice(bgs)).convert("RGB").resize((sz, sz), Image.BILINEAR)
            if random.random() < 0.5: im = ImageEnhance.Brightness(im).enhance(random.uniform(0.6, 1.2))
            return im
        except Exception:
            pass
    return _procedural(sz)

def _paste(canvas, sprite_path, sz):
    try:
        sp = Image.open(sprite_path).convert("RGBA")
    except Exception:
        return None
    scale = random.randint(20, 110)
    sp = sp.resize((scale, max(8, int(scale * sp.height / max(1, sp.width)))), Image.LANCZOS)
    if random.random() < 0.5: sp = sp.transpose(Image.FLIP_LEFT_RIGHT)
    if random.random() < 0.3: sp = ImageEnhance.Brightness(sp).enhance(random.uniform(0.7, 1.25))
    # tight box from the alpha channel (ignore fully transparent margins)
    bbox = sp.getbbox()
    if bbox is None: return None
    sp = sp.crop(bbox)
    w, h = sp.size
    if w < 6 or h < 6 or w >= sz or h >= sz: return None
    ox = random.randint(0, sz - w); oy = random.randint(0, sz - h)
    canvas.paste(sp, (ox, oy), sp)
    cx = (ox + w / 2) / sz; cy = (oy + h / 2) / sz
    return (0, cx, cy, w / sz, h / sz)   # class 0 = entity

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenes", type=int, default=3000)
    ap.add_argument("--val", type=float, default=0.1)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--max-objs", type=int, default=14)
    a = ap.parse_args()

    if os.path.exists(os.path.join(OUT, "data.yaml")) and \
       len(glob.glob(os.path.join(OUT, "images", "train", "*.jpg"))) > 0:
        print("[yolo-data] already generated -> skip (delete tools/ocr-train/yolo to redo)"); return

    sprites = _sprites(); bgs = _bgs()
    if not sprites:
        print("[yolo-data] no sprites in icons/raw/spr_* -> run extract_sprites.py first"); return
    for sub in ("images/train", "images/val", "labels/train", "labels/val"):
        os.makedirs(os.path.join(OUT, sub), exist_ok=True)

    for i in range(a.scenes):
        split = "val" if random.random() < a.val else "train"
        sz = a.imgsz
        canvas = _bg(sz, bgs)
        boxes = []
        for _ in range(random.randint(1, a.max_objs)):
            b = _paste(canvas, random.choice(sprites), sz)
            if b: boxes.append(b)
        if random.random() < 0.2:
            canvas = canvas.filter(ImageFilter.GaussianBlur(random.uniform(0.3, 0.9)))
        stem = "s_%06d" % i
        canvas.convert("RGB").save(os.path.join(OUT, "images", split, stem + ".jpg"), quality=85)
        with open(os.path.join(OUT, "labels", split, stem + ".txt"), "w") as f:
            f.write("\n".join("%d %.6f %.6f %.6f %.6f" % b for b in boxes))
        if i % 500 == 0: print("  scene %d/%d" % (i, a.scenes), flush=True)

    yaml = ("path: %s\ntrain: images/train\nval: images/val\nnc: 1\nnames: ['entity']\n"
            % OUT.replace("\\", "/"))
    open(os.path.join(OUT, "data.yaml"), "w").write(yaml)
    nt = len(glob.glob(os.path.join(OUT, "images", "train", "*.jpg")))
    nv = len(glob.glob(os.path.join(OUT, "images", "val", "*.jpg")))
    print("[yolo-data] generated %d train / %d val scenes -> yolo/" % (nt, nv))

if __name__ == "__main__":
    main()
