"""RO-realistic synthetic text renderer for recognition fine-tuning.

Renders the corpus (HUD numbers, monsters, maps, skills, names) the way RO actually draws text:
small sizes, black outline, drop shadow, coloured fills, on busy/gradient/panel backgrounds. Writes
PaddleOCR rec format (rec_gt.txt: "crops/xxx.png<TAB>text"). Consumed by train_export.py.
"""
import os, glob, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from patterns import ROLE_PATTERNS, sample_value

# RO commonly renders with these Windows fonts; fall back to any TTF in fonts_dir, then PIL default.
_WIN_FONTS = ["arial.ttf", "arialbd.ttf", "tahoma.ttf", "tahomabd.ttf", "verdana.ttf",
              "micross.ttf", "cour.ttf", "gulim.ttc", "msgothic.ttc", "segoeui.ttf"]

def _fonts(fonts_dir):
    paths = []
    for ext in ("*.ttf", "*.TTF", "*.otf", "*.OTF", "*.ttc", "*.TTC"):
        paths += glob.glob(os.path.join(fonts_dir, "**", ext), recursive=True)
    win = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
    if os.path.isdir(win):
        for f in _WIN_FONTS:
            p = os.path.join(win, f)
            if os.path.exists(p):
                paths.append(p)
    return paths or [None]   # None -> PIL default bitmap font

# Foreground colours RO uses for HUD / names / labels.
_FG = [(255, 255, 255), (255, 255, 0), (110, 255, 110), (120, 200, 255),
       (255, 180, 90), (255, 110, 110), (230, 230, 230), (200, 220, 255)]

def _gradient(w, h, c0, c1):
    im = Image.new("RGB", (w, h))
    px = im.load()
    for y in range(h):
        t = y / max(1, h - 1)
        r = int(c0[0] + (c1[0] - c0[0]) * t); g = int(c0[1] + (c1[1] - c0[1]) * t); b = int(c0[2] + (c1[2] - c0[2]) * t)
        for x in range(w): px[x, y] = (r, g, b)
    return im

def _background(w, h):
    kind = random.random()
    if kind < 0.30:                       # dark HUD
        c = random.randint(8, 40); return Image.new("RGB", (w, h), (c, c, c + random.randint(0, 10)))
    if kind < 0.55:                       # bluish Basic-Info panel gradient
        return _gradient(w, h, (180, 200, 230), (120, 150, 200))
    if kind < 0.75:                       # bright panel
        c = random.randint(200, 255); return Image.new("RGB", (w, h), (c, c, c))
    # busy game-scene-ish: random gradient + noise
    im = _gradient(w, h, tuple(random.randint(20, 120) for _ in range(3)), tuple(random.randint(20, 140) for _ in range(3)))
    px = im.load()
    for _ in range((w * h) // 8):
        x = random.randint(0, w - 1); y = random.randint(0, h - 1)
        d = random.randint(-30, 30); r, g, b = px[x, y]
        px[x, y] = (max(0, min(255, r + d)), max(0, min(255, g + d)), max(0, min(255, b + d)))
    return im

def _render(text, font):
    size_probe = Image.new("RGB", (4, 4)); d = ImageDraw.Draw(size_probe)
    stroke = random.choice([0, 1, 1, 2])
    try:
        bbox = d.textbbox((0, 0), text, font=font, stroke_width=stroke)
    except TypeError:
        stroke = 0; bbox = d.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pad = random.randint(2, 7)
    W, H = max(2, tw + pad * 2), max(2, th + pad * 2)
    im = _background(W, H)
    dr = ImageDraw.Draw(im)
    ox, oy = pad - bbox[0], pad - bbox[1]
    # drop shadow
    if random.random() < 0.6:
        dr.text((ox + 1, oy + 1), text, font=font, fill=(0, 0, 0))
    fg = random.choice(_FG)
    outline = (0, 0, 0) if sum(fg) > 200 else (255, 255, 255)
    try:
        dr.text((ox, oy), text, font=font, fill=fg, stroke_width=stroke, stroke_fill=outline)
    except TypeError:
        dr.text((ox, oy), text, font=font, fill=fg)
    # occasional downscale->upscale + blur to mimic RO's tiny scaled text
    if random.random() < 0.5:
        sc = random.uniform(0.6, 0.95)
        im = im.resize((max(2, int(W * sc)), max(2, int(H * sc))), Image.BILINEAR).resize((W, H), Image.BILINEAR)
    if random.random() < 0.35:
        im = im.filter(ImageFilter.GaussianBlur(random.uniform(0.3, 0.9)))
    return im

def generate(out_dir, count=20000, fonts_dir="fonts"):
    img_dir = os.path.join(out_dir, "crops"); os.makedirs(img_dir, exist_ok=True)
    fonts = _fonts(fonts_dir)
    roles = list(ROLE_PATTERNS.keys())
    lines = []
    for i in range(count):
        text = sample_value(random.choice(roles))
        if not text:
            continue
        size = random.choice([9, 10, 11, 11, 12, 12, 13, 14, 16, 18])   # weighted small (RO HUD)
        fp = random.choice(fonts)
        try:
            font = ImageFont.truetype(fp, size) if fp else ImageFont.load_default()
        except Exception:
            font = ImageFont.load_default()
        try:
            im = _render(text, font)
        except Exception:
            continue
        rel = os.path.join("crops", f"s_{i:06d}.png")
        im.convert("RGB").save(os.path.join(out_dir, rel))
        lines.append(f"{rel}\t{text}")
        if (i + 1) % 5000 == 0:
            print(f"  rendered {i+1}/{count}", flush=True)
    with open(os.path.join(out_dir, "rec_gt.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"synth: {len(lines)} samples -> {out_dir}/rec_gt.txt")
    return len(lines)

if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="recdata"); ap.add_argument("--count", type=int, default=20000)
    ap.add_argument("--fonts", default="fonts")
    a = ap.parse_args()
    generate(a.out, a.count, a.fonts)
