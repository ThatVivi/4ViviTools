#!/usr/bin/env python3
"""
extract_sprites.py -- decode Gravity .spr sprites to PNG for the icon/sprite CLASSIFIER.

Covers monsters, job/class bodies, homun, npc, mounts, etc. Writes the LARGEST frame of each
sprite (the clearest full-body pose) as a transparent PNG into icons/raw/, same layout the
classifier expects:  icons/raw/<group>__<name>/0.png   (class label = group__name)

Then build_icon_model.py picks these up automatically alongside the item/skill icons.

Usage (point at the GRF sprite root or any subfolder):
  python tools/ocr-train/extract_sprites.py --src "../../GRF/data/sprite"
  python tools/ocr-train/extract_sprites.py --src "../../GRF/data/sprite/䜜仃來"   # monsters only

Notes:
- Class labels are the sprite FILE names (often Korean resource names). Mapping those to English
  monster/job names is a separate lookup; for now the classifier learns "this picture = this sprite".
- SPR format: v1.x raw indexed, v2.0 adds RGBA frames, v2.1 adds RLE. Palette = last 1024 bytes.
"""
import argparse, glob, os, struct
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "icons", "raw")

def _decode_spr(path):
    with open(path, "rb") as f:
        buf = f.read()
    if len(buf) < 8 or buf[0:2] != b"SP":
        return []
    ver = struct.unpack_from("<H", buf, 2)[0]          # 0x0101=1.1, 0x0200=2.0, 0x0201=2.1
    major, minor = ver >> 8, ver & 0xFF
    off = 4
    n_idx = struct.unpack_from("<H", buf, off)[0]; off += 2
    n_rgba = 0
    if ver >= 0x0200:
        n_rgba = struct.unpack_from("<H", buf, off)[0]; off += 2
    # palette = last 1024 bytes (256 * RGBA)
    pal = buf[-1024:] if len(buf) >= 1024 else b"\x00" * 1024
    frames = []

    def put_indexed(w, h, indices):
        img = Image.new("RGBA", (w, h))
        px = img.load()
        for i, idx in enumerate(indices[: w * h]):
            x, y = i % w, i // w
            if idx == 0:
                px[x, y] = (0, 0, 0, 0)
            else:
                b0 = idx * 4
                px[x, y] = (pal[b0], pal[b0 + 1], pal[b0 + 2], 255)
        return img

    try:
        for _ in range(n_idx):
            w, h = struct.unpack_from("<HH", buf, off); off += 4
            if ver >= 0x0201:                              # RLE
                dsize = struct.unpack_from("<H", buf, off)[0]; off += 2
                end = off + dsize; idx = bytearray()
                while off < end:
                    c = buf[off]; off += 1
                    idx.append(c)
                    if c == 0:                              # run of transparent
                        run = buf[off]; off += 1
                        idx.extend(b"\x00" * (run - 1))
                indices = bytes(idx)
            else:                                          # raw
                indices = buf[off: off + w * h]; off += w * h
            if w and h:
                frames.append(put_indexed(w, h, indices))
        for _ in range(n_rgba):
            w, h = struct.unpack_from("<HH", buf, off); off += 4
            raw = buf[off: off + w * h * 4]; off += w * h * 4
            if w and h:
                frames.append(Image.frombytes("RGBA", (w, h), raw))
    except Exception:
        pass
    return frames

DEFAULT_SRC = os.path.normpath(os.path.join(HERE, "..", "..", "GRF", "data", "sprite"))

# category -> GRF sprite subfolder (Korean names in the real client)
CATS = {
    "monsters": "\uBAAC\uC2A4\uD130",   # 몬스터
    "mounts":   "\uBAAC\uC2A4\uD130",   # mounts (peco/warg/dragon/mado) live in the monster folder
    "jobs":     "\uC778\uAC04\uC871",   # 인간족  (player/job bodies)
    "classes":  "\uC778\uAC04\uC871",
    "doram":    "\uB3C4\uB78C\uC871",   # 도람족
    "homun":    "homun",
    "npc":      "npc",
    "robes":    "\uB85C\uBE0C",          # 로브
    "shields":  "\uBC29\uD328",          # 방패
    "items":    "\uC544\uC774\uD15C",   # 아이템 (dropped-item sprites)
    "acc":      "\uC545\uC138\uC0AC\uB9AC",  # 악세사리
    "effects":  "\uC774\uD329\uD2B8",   # 이팩트
}

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=DEFAULT_SRC,
                    help="folder with .spr files (default: <project>/GRF/data/sprite)")
    ap.add_argument("--only", default="",
                    help="comma list of categories: monsters,jobs,mounts,homun,doram,npc,robes,shields,items,acc,effects")
    ap.add_argument("--limit", type=int, default=0, help="0 = all")
    a = ap.parse_args()
    existing = glob.glob(os.path.join(RAW, "spr_*"))
    if existing:
        print("sprites already extracted (%d) -> skipping. Delete icons/raw/spr_* folders to redo." % len(existing))
        return
    src = a.src if os.path.isabs(a.src) else os.path.abspath(a.src)
    print("source:", src)
    if not os.path.isdir(src):
        print("ERROR: folder not found. Pass --src with the path to your GRF sprite folder.")
        return
    sprs = glob.glob(os.path.join(src, "**", "*.spr"), recursive=True)
    print("found %d .spr files" % len(sprs))
    if a.only:
        keys = [k.strip().lower() for k in a.only.split(",") if k.strip()]
        subs = [CATS[k] for k in keys if k in CATS]
        unknown = [k for k in keys if k not in CATS]
        if unknown: print("WARN: unknown categories ignored:", unknown)
        before = len(sprs)
        sprs = [p for p in sprs if any(("/" + s + "/") in p.replace("\\", "/") for s in subs)]
        print("filter --only %s -> %d of %d sprites" % (",".join(keys), len(sprs), before))
    if not sprs:
        print("ERROR: no .spr matched.")
        return
    if a.limit: sprs = sprs[: a.limit]
    os.makedirs(RAW, exist_ok=True)
    ok = 0
    for p in sprs:
        frames = _decode_spr(p)
        if not frames:
            continue
        best = max(frames, key=lambda im: im.width * im.height)   # clearest full-body frame
        group = os.path.basename(os.path.dirname(p)) or "spr"
        name = os.path.splitext(os.path.basename(p))[0]
        safe = "".join(ch if ch.isalnum() else "_" for ch in (group + "__" + name))
        d = os.path.join(RAW, "spr_" + safe); os.makedirs(d, exist_ok=True)
        best.save(os.path.join(d, "0.png"))
        ok += 1
        if ok % 500 == 0: print("decoded", ok, flush=True)
    print("decoded %d / %d sprites -> icons/raw" % (ok, len(sprs)))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
