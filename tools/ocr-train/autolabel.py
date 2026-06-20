import os, glob, json, re
from PIL import Image

def crop_roles(image_path, template):
    im = Image.open(image_path); W, H = im.size
    out = []
    for m in template["marks"]:
        if m.get("isBar"): continue
        x0 = int(m["x"] * W); y0 = int(m["y"] * H)
        x1 = int((m["x"] + m["w"]) * W); y1 = int((m["y"] + m["h"]) * H)
        out.append({"role": m["role"], "box": (x0, y0, x1, y1), "isText": bool(m.get("isText"))})
    return out

def _constrain(role, text, is_text):
    if is_text: return text.strip()
    return re.sub(r"[^0-9/.]", "", text)

def label_folder(user_dir, template_path, out_dir, ocr_read):
    os.makedirs(os.path.join(out_dir, "crops"), exist_ok=True)
    template = json.load(open(template_path, encoding="utf-8"))
    lines, idx = [], 0
    imgs = glob.glob(os.path.join(user_dir, "*.png")) + glob.glob(os.path.join(user_dir, "*.jpg"))
    for img in imgs:
        full = Image.open(img)
        for c in crop_roles(img, template):
            crop = full.crop(c["box"])
            text = _constrain(c["role"], ocr_read(crop), c["isText"])
            rel = os.path.join("crops", f"r_{idx:05d}.png"); idx += 1
            crop.save(os.path.join(out_dir, rel))
            lines.append(f"{rel}\t{text if text else '###'}")
    with open(os.path.join(out_dir, "rec_gt.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return len(lines)
