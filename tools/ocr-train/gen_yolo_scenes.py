#!/usr/bin/env python3
"""
gen_yolo_scenes.py -- self-labeled synthetic RO scenes for the entity detector.

The paid Roboflow datasets remain the main detector data. This generator fills the gap the live
bot cares about: the same monster appearing in many sprite frames/directions while the whole map
scrolls under the camera and the UI stays fixed. It composites GRF monster frames over map-like
backgrounds, adds fixed HUD negatives, mild occlusion, scale/brightness/blur variation, and writes
YOLO labels compatible with the real merged detector classes.

Output: tools/ocr-train/yolo/{images,labels}/{train,val} + data.yaml
Classes: 0 monster  1 player  2 loot  3 portal  4 target  5 target_hp  6 player_hp
"""
import argparse
import glob
import json
import os
import random
import shutil
import traceback
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "icons", "raw")
OUT = os.path.join(HERE, "yolo")
MONSTER_MANIFEST = os.path.join(HERE, "icons", "monster_manifest.json")
CLASSES = ["monster", "player", "loot", "portal", "target", "target_hp", "player_hp"]

RO_NAME_BITS = ("Poring", "Poporing", "Wolf", "Spore", "Zombie", "Skeleton", "Boa", "Mantis",
                "Dead Willow", "Elder Willow", "Familiar", "Flora", "Geographer")


def _sprite_frames():
    """Return monster frames first. Direction/pose variety is the point."""
    frames = []
    try:
        manifest = json.load(open(MONSTER_MANIFEST, encoding="utf-8"))
        labels = [c.get("label", "") for c in manifest.get("classes", []) if c.get("label", "").startswith("mob__")]
        for label in labels:
            frames.extend(glob.glob(os.path.join(RAW, label, "*.png")))
        if frames:
            return frames
    except Exception:
        pass
    for pattern in ("mob__*", "monster__*", "monsters__*"):
        for d in glob.glob(os.path.join(RAW, pattern)):
            if os.path.isdir(d):
                frames.extend(glob.glob(os.path.join(d, "*.png")))
    if not frames:
        # Fallback for older extracted banks. This can include non-monsters, so only use it when the
        # curated mob__ bank has not been generated yet.
        for d in glob.glob(os.path.join(RAW, "spr_*")):
            if os.path.isdir(d):
                frames.extend(glob.glob(os.path.join(d, "*.png")))
    return frames


def _bgs():
    return glob.glob(os.path.join(RAW, "map__*", "*.png"))


def _procedural(sz):
    im = Image.new("RGB", (sz, sz))
    px = im.load()
    base = random.choice(((28, 66, 38), (72, 63, 42), (46, 58, 72), (58, 48, 38), (35, 72, 72)))
    for y in range(sz):
        for x in range(sz):
            n = random.randint(-22, 22)
            stripe = ((x // random.randint(28, 54)) + (y // random.randint(22, 48))) % 2
            shade = 14 if stripe else 0
            px[x, y] = tuple(max(0, min(160, c + n + shade)) for c in base)
    return im.filter(ImageFilter.GaussianBlur(random.uniform(0.2, 0.8)))


def _bg(sz, bgs, scroll=None):
    if bgs and random.random() < 0.72:
        try:
            im = Image.open(random.choice(bgs)).convert("RGB")
            # Crop with an offset so generated frames look like the camera moved over the map.
            scale = random.uniform(2.5, 6.0)
            big = im.resize((max(sz, int(im.width * scale)), max(sz, int(im.height * scale))), Image.BILINEAR)
            max_x = max(0, big.width - sz)
            max_y = max(0, big.height - sz)
            sx, sy = scroll or (random.randint(0, max_x), random.randint(0, max_y))
            sx = max(0, min(max_x, sx))
            sy = max(0, min(max_y, sy))
            im = big.crop((sx, sy, sx + sz, sy + sz))
            im = ImageEnhance.Brightness(im).enhance(random.uniform(0.65, 1.22))
            im = ImageEnhance.Contrast(im).enhance(random.uniform(0.85, 1.25))
            return im
        except Exception:
            pass
    return _procedural(sz)


def _alpha_tight(sp):
    bbox = sp.getbbox()
    if bbox is None:
        return None
    sp = sp.crop(bbox)
    if sp.width < 6 or sp.height < 6:
        return None
    return sp


def _transform_sprite(sprite_path):
    try:
        sp = Image.open(sprite_path).convert("RGBA")
    except Exception:
        return None
    sp = _alpha_tight(sp)
    if sp is None:
        return None

    if random.random() < 0.5:
        sp = sp.transpose(Image.FLIP_LEFT_RIGHT)
    # Include tiny/far monsters. Roboflow's stronger map-specific sets are real screenshots, where
    # monsters are often much smaller than the icon bank crops.
    scale = random.randint(14, 118)
    sp = sp.resize((scale, max(8, int(scale * sp.height / max(1, sp.width)))), Image.LANCZOS)
    if random.random() < 0.35:
        sp = ImageEnhance.Brightness(sp).enhance(random.uniform(0.72, 1.28))
    if random.random() < 0.25:
        sp = ImageEnhance.Contrast(sp).enhance(random.uniform(0.82, 1.35))
    if random.random() < 0.12:
        sp = sp.filter(ImageFilter.GaussianBlur(random.uniform(0.25, 0.7)))
    return _alpha_tight(sp)


def _motion_blur(im, radius=None):
    # Pillow's built-in Kernel filter only accepts small odd kernels reliably.
    radius = radius if radius is not None else random.choice((3, 5))
    if radius not in (3, 5):
        radius = 5 if radius > 3 else 3
    if radius <= 1:
        return im
    horiz = random.random() < 0.65
    kernel = [0.0] * (radius * radius)
    mid = radius // 2
    for i in range(radius):
        kernel[mid * radius + i if horiz else i * radius + mid] = 1.0 / radius
    return im.filter(ImageFilter.Kernel((radius, radius), kernel, scale=1.0))


def _draw_floating_noise(canvas, bbox):
    """Draw RO floating text/damage above or near a monster. These are not labels."""
    draw = ImageDraw.Draw(canvas, "RGBA")
    x0, y0, x1, y1 = bbox
    if random.random() < 0.55:
        text = str(random.choice((12, 47, 103, 590, 1000, 1832, 9999)))
        tx = int((x0 + x1) / 2) + random.randint(-18, 18)
        ty = max(0, y0 - random.randint(6, 26))
        draw.text((tx, ty), text, fill=random.choice(((255, 240, 64, 230), (255, 80, 80, 230), (245, 245, 245, 210))))
    if random.random() < 0.42:
        name = random.choice(RO_NAME_BITS)
        tx = max(0, int((x0 + x1) / 2) - random.randint(16, 48))
        ty = max(0, y0 - random.randint(18, 42))
        draw.text((tx + 1, ty + 1), name, fill=(0, 0, 0, 150))
        draw.text((tx, ty), name, fill=(235, 235, 235, 220))
    if random.random() < 0.32:
        # target/monster HP bar above the sprite. It should not inflate the monster box.
        tx0 = max(0, int((x0 + x1) / 2) - random.randint(18, 36))
        ty0 = max(0, y0 - random.randint(3, 15))
        tw = random.randint(28, 70)
        draw.rectangle((tx0, ty0, tx0 + tw, ty0 + 4), fill=(35, 14, 16, 190))
        draw.rectangle((tx0 + 1, ty0 + 1, tx0 + random.randint(6, tw), ty0 + 3), fill=(210, 35, 48, 230))


def _paste_monster(canvas, sprite_path, sz):
    sp = _transform_sprite(sprite_path)
    if sp is None:
        return None
    w, h = sp.size
    if w >= sz or h >= sz:
        return None
    ox = random.randint(-w // 4, sz - max(2, int(w * 0.75)))
    oy = random.randint(18, sz - max(2, int(h * 0.60)))
    canvas.paste(sp, (ox, oy), sp)

    # Sometimes another player/effect/UI piece covers part of the sprite; teach YOLO not to require
    # a perfect full frame.
    if random.random() < 0.22:
        draw = ImageDraw.Draw(canvas, "RGBA")
        cx = ox + random.randint(0, max(1, w))
        cy = oy + random.randint(0, max(1, h))
        draw.rectangle((cx - w // 5, cy - h // 5, cx + w // 4, cy + h // 6),
                       fill=(20, 20, 20, random.randint(45, 120)))

    x0 = max(0, ox)
    y0 = max(0, oy)
    x1 = min(sz, ox + w)
    y1 = min(sz, oy + h)
    if x1 - x0 < 8 or y1 - y0 < 8:
        return None
    _draw_floating_noise(canvas, (x0, y0, x1, y1))
    cx = ((x0 + x1) / 2) / sz
    cy = ((y0 + y1) / 2) / sz
    return (0, cx, cy, (x1 - x0) / sz, (y1 - y0) / sz)


def _fixed_hud_negatives(canvas, sz):
    """Draw RO-like UI that stays fixed while the scene moves. No labels: these are negatives."""
    draw = ImageDraw.Draw(canvas, "RGBA")
    if random.random() < 0.88:
        draw.rectangle((0, sz - 64, sz, sz), fill=(16, 16, 24, random.randint(120, 190)))
        for i in range(random.randint(6, 14)):
            x = 12 + i * 38
            draw.rectangle((x, sz - 52, x + 30, sz - 22), outline=(220, 210, 170, 120), fill=(40, 38, 44, 120))
    if random.random() < 0.7:
        draw.rectangle((8, 8, random.randint(150, 260), 54), fill=(20, 20, 24, 150))
        draw.rectangle((20, 18, random.randint(90, 190), 26), fill=(180, 32, 42, 180))
        draw.rectangle((20, 32, random.randint(75, 170), 40), fill=(50, 70, 190, 170))
    if random.random() < 0.55:
        # RO chat/system text and random numbers are common false positives. Leave them unlabeled.
        for _ in range(random.randint(2, 7)):
            x = random.randint(8, max(9, sz - 180))
            y = random.randint(60, max(61, sz - 90))
            text = random.choice(("Base EXP +0.1%", "Job EXP +0.1%", "1000", "Miss", "Critical", "Zeny", "HP", "SP"))
            draw.text((x + 1, y + 1), text, fill=(0, 0, 0, 150))
            draw.text((x, y), text, fill=random.choice(((235, 235, 235, 180), (255, 220, 90, 200), (90, 190, 255, 190))))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenes", type=int, default=3000)
    ap.add_argument("--val", type=float, default=0.1)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--max-objs", type=int, default=14)
    ap.add_argument("--negative-scenes", type=float, default=0.08,
                    help="fraction of scenes with no monsters; hard negatives for UI/text/background")
    ap.add_argument("--force", action="store_true", help="regenerate even when yolo/data.yaml already exists")
    ap.add_argument("--resume", action="store_true", help="keep existing generated scene files and fill missing indices")
    a = ap.parse_args()

    if a.force and a.resume:
        raise SystemExit("--force and --resume cannot be used together")

    if not a.force and os.path.exists(os.path.join(OUT, "data.yaml")) and glob.glob(os.path.join(OUT, "images", "train", "*.jpg")):
        if not a.resume:
            print("[yolo-data] already generated -> skip (pass --force, --resume, or delete tools/ocr-train/yolo to redo)")
            return
    if a.force and os.path.isdir(OUT):
        shutil.rmtree(OUT)

    sprites = _sprite_frames()
    bgs = _bgs()
    if not sprites:
        print("[yolo-data] no monster frames in icons/raw/mob__* -> run build_monster_names.py first")
        return
    print("[yolo-data] using %d monster frames from icons/raw" % len(sprites))

    for sub in ("images/train", "images/val", "labels/train", "labels/val"):
        os.makedirs(os.path.join(OUT, sub), exist_ok=True)

    skipped_existing = 0
    generated = 0
    failed = 0
    for i in range(a.scenes):
        existing = glob.glob(os.path.join(OUT, "images", "*", "s_%06d.jpg" % i))
        if a.resume and existing:
            skipped_existing += 1
            if i % 500 == 0:
                print("  scene %d/%d (resume skip)" % (i, a.scenes), flush=True)
            continue
        split = "val" if random.random() < a.val else "train"
        sz = a.imgsz
        try:
            canvas = _bg(sz, bgs)
            boxes = []
            negative_scene = random.random() < max(0.0, min(0.35, a.negative_scenes))
            if not negative_scene:
                for _ in range(random.randint(1, a.max_objs)):
                    b = _paste_monster(canvas, random.choice(sprites), sz)
                    if b:
                        boxes.append(b)
            _fixed_hud_negatives(canvas, sz)
            if random.random() < 0.18:
                canvas = canvas.filter(ImageFilter.GaussianBlur(random.uniform(0.25, 0.85)))
            if random.random() < 0.20:
                canvas = _motion_blur(canvas)
            if random.random() < 0.18:
                canvas = ImageEnhance.Sharpness(canvas).enhance(random.uniform(0.55, 1.45))

            stem = "s_%06d" % i
            canvas.convert("RGB").save(os.path.join(OUT, "images", split, stem + ".jpg"), quality=random.randint(78, 92))
            with open(os.path.join(OUT, "labels", split, stem + ".txt"), "w", encoding="utf-8") as f:
                f.write("\n".join("%d %.6f %.6f %.6f %.6f" % b for b in boxes))
            generated += 1
        except Exception:
            failed += 1
            print("[yolo-data] scene %d failed; skipping and continuing" % i, flush=True)
            traceback.print_exc()
        if i % 500 == 0:
            print("  scene %d/%d" % (i, a.scenes), flush=True)

    yaml = ("path: %s\ntrain: images/train\nval: images/val\nnc: %d\nnames: [%s]\n"
            % (OUT.replace("\\", "/"), len(CLASSES), ", ".join("'%s'" % c for c in CLASSES)))
    with open(os.path.join(OUT, "data.yaml"), "w", encoding="utf-8") as f:
        f.write(yaml)
    nt = len(glob.glob(os.path.join(OUT, "images", "train", "*.jpg")))
    nv = len(glob.glob(os.path.join(OUT, "images", "val", "*.jpg")))
    print("[yolo-data] generated %d train / %d val scenes -> yolo/ (new=%d skipped=%d failed=%d)"
          % (nt, nv, generated, skipped_existing, failed))
    if nt + nv < max(1, int(a.scenes * 0.92)):
        raise SystemExit("[yolo-data] too few scenes generated: %d/%d" % (nt + nv, a.scenes))


if __name__ == "__main__":
    main()
