import os, glob, random
from PIL import Image, ImageDraw, ImageFont
from patterns import ROLE_PATTERNS, sample_value

def _fonts(fonts_dir):
    paths = []
    for ext in ("*.ttf", "*.TTF", "*.otf", "*.OTF"):
        paths += glob.glob(os.path.join(fonts_dir, "**", ext), recursive=True)
    return paths

def generate(out_dir, count=5000, fonts_dir="fonts"):
    img_dir = os.path.join(out_dir, "crops"); os.makedirs(img_dir, exist_ok=True)
    fonts = _fonts(fonts_dir)
    roles = list(ROLE_PATTERNS.keys())
    lines = []
    for i in range(count):
        text = sample_value(random.choice(roles))
        size = random.randint(14, 28)
        try:
            font = ImageFont.truetype(random.choice(fonts), size) if fonts else ImageFont.load_default()
        except Exception:
            font = ImageFont.load_default()
        pad = random.randint(2, 6)
        tmp = Image.new("RGB", (4, 4)); d = ImageDraw.Draw(tmp)
        bbox = d.textbbox((0, 0), text, font=font); tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        bg = random.choice([(20, 20, 28), (0, 0, 0), (40, 40, 40), (255, 255, 255)])
        fg = (255, 255, 255) if sum(bg) < 360 else (0, 0, 0)
        im = Image.new("RGB", (max(1, tw + pad * 2), max(1, th + pad * 2)), bg)
        ImageDraw.Draw(im).text((pad - bbox[0], pad - bbox[1]), text, font=font, fill=fg)
        rel = os.path.join("crops", f"s_{i:05d}.png")
        im.save(os.path.join(out_dir, rel))
        lines.append(f"{rel}\t{text}")
    with open(os.path.join(out_dir, "rec_gt.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return count
