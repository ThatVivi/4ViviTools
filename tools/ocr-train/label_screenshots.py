#!/usr/bin/env python3
"""
label_screenshots.py -- turn real gameplay screenshots into labeled OCR line-crops.

A whole screenshot can't train a line recognizer. This runs the *already installed* PaddleOCR
(detector + recognizer) over each screenshot, crops every detected text line, and writes:

    real/crops/r_000001.png ...
    real/rec_gt.txt          ->  "crops/r_000001.png<TAB><predicted text>"

The predictions are a STARTING POINT. Open real/rec_gt.txt and fix any wrong lines (that is the
high-value step -- corrected real text is what lifts accuracy). Then run.py merges real/ in.

Usage:
    python tools/ocr-train/label_screenshots.py --src user_images        # scans *.jpg/*.png/*.webp
    python tools/ocr-train/label_screenshots.py --src <your screenshots folder>

Tip: use real in-game UI shots (HP/SP bars, monster name labels, inventory, skill window),
NOT promo art -- art has no readable game text.
"""
import argparse, glob, os
from PIL import Image

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "user_images"))
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "real"))
    ap.add_argument("--min-conf", type=float, default=0.5)
    a = ap.parse_args()

    dpath = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ppocrv5_latin_dict.txt")
    DICT = set(open(dpath, encoding="utf-8").read().splitlines()) | {" "}
    def in_dict(t): return t and all(c in DICT for c in t)

    from paddleocr import PaddleOCR
    ocr = PaddleOCR(use_angle_cls=False, lang="latin", show_log=False)

    crops = os.path.join(a.out, "crops"); os.makedirs(crops, exist_ok=True)
    imgs = []
    for ext in ("*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"):
        imgs += glob.glob(os.path.join(a.src, ext))
    imgs = [p for p in imgs if "/crops/" not in p.replace("\\", "/")]
    print("screenshots found:", len(imgs))

    rows, idx = [], 0
    for p in imgs:
        try:
            im = Image.open(p).convert("RGB")
        except Exception as e:
            print("skip", os.path.basename(p), e); continue
        res = ocr.ocr(p, cls=False)
        if not res or not res[0]:
            print("no text:", os.path.basename(p)); continue
        for line in res[0]:
            box, (txt, conf) = line[0], line[1]
            if not txt or conf < a.min_conf:
                continue
            if not in_dict(txt.strip()):   # drop non-latin (e.g. Russian) -> would corrupt labels
                continue
            xs = [pt[0] for pt in box]; ys = [pt[1] for pt in box]
            x0, y0, x1, y1 = int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))
            if x1 - x0 < 4 or y1 - y0 < 4:
                continue
            rel = "crops/r_%06d.png" % idx; idx += 1
            im.crop((x0, y0, x1, y1)).save(os.path.join(a.out, rel))
            rows.append("%s\t%s" % (rel, txt.strip()))
    open(os.path.join(a.out, "rec_gt.txt"), "w", encoding="utf-8").write("\n".join(rows) + "\n")
    print("wrote %d line-crops -> %s" % (len(rows), os.path.join(a.out, "rec_gt.txt")))
    print("NOW: open real/rec_gt.txt and fix any wrong text, then run run.py.")

if __name__ == "__main__":
    main()
