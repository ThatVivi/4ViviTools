#!/usr/bin/env python3
"""
build_monster_names.py -- add a curated MONSTER sprite set to the icon recognizer.

What it does (double-click to run):
  1) For each .spr/.act pair in the source folder, render the IDLE pose (.act action 0, frame 0)
     by compositing the referenced .spr clips -> a clean transparent PNG.
     Falls back to the largest .spr frame if the .act can't be parsed.
        -> icons/raw/mob__<name>/0.png      (so a future full retrain also includes them)
  2) Because the recognizer is metric-learning (ArcFace embedding + nearest-neighbor), NEW icons
     need NO retrain: it embeds each rendered PNG with the existing icon_embedder.onnx and APPENDS
     the 256-D vectors to the reference bank (icon_refs.bin + labels.txt + icon_meta.json).
     The monsters become recognizable immediately. Names already in the bank are skipped.

Default source: tools/ocr-train/OCR Names/data/sprite/몬스터
Override:  python build_monster_names.py --src "C:\\path\\to\\sprite\\folder"

The label is the sprite FILE name (e.g. mob__agav). The Smart Bot matches monster rules by
substring, so a rule Name="agav" will fire on it. (.spr has no text; the name-above-head is the
display name, mapped separately if needed.)
"""
import argparse, glob, os, struct, sys, subprocess, json, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.join(HERE, "icons")
RAW = os.path.join(ICONS, "raw")
OUT_DIR = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "icons")
DEFAULT_SRC = os.path.join(HERE, "OCR Names", "data", "sprite", "몬스터")  # 몬스터

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
MONSTER_FOLDER = "\uBAAC\uC2A4\uD130"
DEFAULT_SRC_CANDIDATES = [
    os.path.join(os.path.dirname(REPO), "claude", "data", "sprite", "¸ó½ºÅÍ"),
    os.path.join(os.path.dirname(REPO), "claude", "data", "sprite", MONSTER_FOLDER),
    os.path.join(REPO, "GRF", "data", "sprite", MONSTER_FOLDER),
    os.path.join(REPO, "GRF", "kRO Data", "data", "sprite", MONSTER_FOLDER),
    os.path.join(HERE, "OCR Names", "data", "sprite", MONSTER_FOLDER),
    DEFAULT_SRC,
]

def _count_spr(path):
    try:
        return sum(1 for _ in glob.iglob(os.path.join(path, "**", "*.spr"), recursive=True))
    except Exception:
        return 0

def _claude_sprite_dirs():
    root = os.path.join(os.path.dirname(REPO), "claude", "data", "sprite")
    if not os.path.isdir(root):
        return []
    dirs = []
    for name in os.listdir(root):
        p = os.path.join(root, name)
        if os.path.isdir(p) and _count_spr(p) > 0:
            dirs.append(p)
    return sorted(dirs, key=_count_spr, reverse=True)

def default_src():
    for p in _claude_sprite_dirs() + DEFAULT_SRC_CANDIDATES:
        if os.path.isdir(p):
            return p
    return DEFAULT_SRC_CANDIDATES[0]

def log(m): print(m, flush=True)

def ensure(mod, pip_name=None):
    try:
        return __import__(mod)
    except Exception:
        subprocess.call([sys.executable, "-m", "pip", "install", "--quiet",
                         "--break-system-packages", pip_name or mod])
        return __import__(mod)

PIL = ensure("PIL", "pillow"); from PIL import Image
np = ensure("numpy")

# ----------------------------------------------------------------- .spr decode
def decode_spr(path):
    """Return (indexed_frames, rgba_frames) as lists of RGBA PIL images."""
    with open(path, "rb") as f:
        buf = f.read()
    if len(buf) < 8 or buf[0:2] != b"SP":
        return [], []
    ver = struct.unpack_from("<H", buf, 2)[0]
    off = 4
    n_idx = struct.unpack_from("<H", buf, off)[0]; off += 2
    n_rgba = 0
    if ver >= 0x0200:
        n_rgba = struct.unpack_from("<H", buf, off)[0]; off += 2
    pal = buf[-1024:] if len(buf) >= 1024 else b"\x00" * 1024
    idx_frames, rgba_frames = [], []

    def put_indexed(w, h, indices):
        img = Image.new("RGBA", (w, h)); px = img.load()
        for i, ci in enumerate(indices[: w * h]):
            x, y = i % w, i // w
            if ci == 0: px[x, y] = (0, 0, 0, 0)
            else:
                b0 = ci * 4; px[x, y] = (pal[b0], pal[b0 + 1], pal[b0 + 2], 255)
        return img

    try:
        for _ in range(n_idx):
            w, h = struct.unpack_from("<HH", buf, off); off += 4
            if ver >= 0x0201:
                dsize = struct.unpack_from("<H", buf, off)[0]; off += 2
                end = off + dsize; idx = bytearray()
                while off < end:
                    c = buf[off]; off += 1; idx.append(c)
                    if c == 0:
                        run = buf[off]; off += 1; idx.extend(b"\x00" * (run - 1))
                indices = bytes(idx)
            else:
                indices = buf[off: off + w * h]; off += w * h
            idx_frames.append(put_indexed(w, h, indices) if (w and h) else None)
        for _ in range(n_rgba):
            w, h = struct.unpack_from("<HH", buf, off); off += 4
            rawpx = buf[off: off + w * h * 4]; off += w * h * 4
            rgba_frames.append(Image.frombytes("RGBA", (w, h), rawpx) if (w and h) else None)
    except Exception:
        pass
    return idx_frames, rgba_frames

# ----------------------------------------------------------------- .act frame0
def act_first_frame_clips(path, ver_out):
    """Parse ACT header -> action 0 -> frame 0 -> list of clips. Only the first frame is parsed
    (no need to skip version-dependent event/anchor trailers of later frames)."""
    with open(path, "rb") as f:
        buf = f.read()
    if len(buf) < 16 or buf[0:2] != b"AC":
        return None
    ver = struct.unpack_from("<H", buf, 2)[0]; ver_out[0] = ver
    off = 4
    n_act = struct.unpack_from("<H", buf, off)[0]; off += 2
    off += 10  # reserved
    if n_act <= 0: return None
    # action 0
    n_frames = struct.unpack_from("<I", buf, off)[0]; off += 4
    if n_frames <= 0: return None
    # frame 0
    off += 32 + 32  # range1 + range2
    n_clips = struct.unpack_from("<I", buf, off)[0]; off += 4
    if n_clips <= 0 or n_clips > 256: return []
    clips = []
    for _ in range(n_clips):
        x, y, spr_no, mirror = struct.unpack_from("<iiii", buf, off); off += 16
        color = (255, 255, 255, 255); sx = sy = 1.0; rot = 0; spr_type = 0
        if ver >= 0x0200:
            r, g, b, a = struct.unpack_from("<BBBB", buf, off); off += 4
            color = (r, g, b, a)
            sx = struct.unpack_from("<f", buf, off)[0]; off += 4
            if ver >= 0x0204:
                sy = struct.unpack_from("<f", buf, off)[0]; off += 4
            else:
                sy = sx
            rot = struct.unpack_from("<i", buf, off)[0]; off += 4
            spr_type = struct.unpack_from("<i", buf, off)[0]; off += 4
            if ver >= 0x0205:
                off += 8  # width, height (unused; we use the real frame size)
        clips.append((x, y, spr_no, mirror, sx, sy, rot, spr_type))
    return clips

def render_from_act(spr_path, act_path):
    idx_frames, rgba_frames = decode_spr(spr_path)
    if not idx_frames and not rgba_frames:
        return None
    clips = None
    try:
        clips = act_first_frame_clips(act_path, [0])
    except Exception:
        clips = None
    if not clips:  # fallback: largest single frame
        allf = [f for f in (idx_frames + rgba_frames) if f is not None]
        return max(allf, key=lambda im: im.width * im.height) if allf else None

    SZ = 256; cx0 = cy0 = SZ // 2
    canvas = Image.new("RGBA", (SZ, SZ), (0, 0, 0, 0))
    drew = False
    for (x, y, spr_no, mirror, sx, sy, rot, spr_type) in clips:
        bank = rgba_frames if spr_type == 1 else idx_frames
        if spr_no < 0 or spr_no >= len(bank) or bank[spr_no] is None:
            continue
        im = bank[spr_no]
        if abs(sx - 1.0) > 0.01 or abs(sy - 1.0) > 0.01:
            nw, nh = max(1, int(round(im.width * sx))), max(1, int(round(im.height * sy)))
            im = im.resize((nw, nh), Image.LANCZOS)
        if mirror:
            im = im.transpose(Image.FLIP_LEFT_RIGHT)
        if rot:
            im = im.rotate(-rot, expand=True, resample=Image.BICUBIC)
        px = cx0 + x - im.width // 2
        py = cy0 + y - im.height // 2
        canvas.alpha_composite(im, (max(0, px), max(0, py)))
        drew = True
    if not drew:
        allf = [f for f in (idx_frames + rgba_frames) if f is not None]
        return max(allf, key=lambda im: im.width * im.height) if allf else None
    bbox = canvas.getbbox()
    return canvas.crop(bbox) if bbox else canvas

# ----------------------------------------------------------------- multi-frame
def _frame_hash(im):
    g = im.convert("L").resize((8, 8))
    px = list(g.getdata()); avg = sum(px) / max(1, len(px))
    return tuple(1 if p > avg else 0 for p in px)

def extra_frames(spr_path, k=6):
    """Distinct poses straight from the .spr (covers the monster turning: front/back/sides), so the
    recognizer matches it from any direction instead of only the single idle pose. k<=0 keeps every
    distinct valid frame."""
    idx_frames, rgba_frames = decode_spr(spr_path)
    allf = [x for x in (idx_frames + rgba_frames) if x is not None and x.width >= 12 and x.height >= 12]
    seen = set(); out = []
    for fr in sorted(allf, key=lambda im: -(im.width * im.height)):
        bbox = fr.getbbox(); c = fr.crop(bbox) if bbox else fr
        if c.width < 12 or c.height < 12:
            continue
        h = _frame_hash(c)
        if h in seen:
            continue
        seen.add(h); out.append(c)
        if k > 0 and len(out) >= k:
            break
    return out

# ----------------------------------------------------------------- embed+append
def preprocess(img, size):
    im = img.convert("RGB").resize((size, size), Image.LANCZOS)
    a = (np.asarray(im, "float32") / 255.0 - 0.5) / 0.5
    return a.transpose(2, 0, 1)

DEFAULT_TARGET_REFS = 12

def append_to_bank(new_classes, target_refs=DEFAULT_TARGET_REFS):
    """new_classes: list of (label, png_path). Embeds + appends to OUT_DIR bank/labels/meta."""
    onnx = os.path.join(OUT_DIR, "icon_embedder.onnx")
    binp = os.path.join(OUT_DIR, "icon_refs.bin")
    labp = os.path.join(OUT_DIR, "labels.txt")
    metap = os.path.join(OUT_DIR, "icon_meta.json")
    if not (os.path.exists(onnx) and os.path.exists(binp) and os.path.exists(labp) and os.path.exists(metap)):
        log("[append] embedder/bank not found in %s -> skipping live append." % OUT_DIR)
        log("         Rendered PNGs are in icons/raw; run build_icon_model.py to fold them in.")
        return
    ort = ensure("onnxruntime")
    meta = json.load(open(metap))
    emb, img = int(meta.get("emb", 256)), int(meta.get("img", 64))
    counts = {}
    max_idx = -1
    for line in open(labp, encoding="utf-8"):
        if "\t" not in line: continue
        i, name = line.rstrip("\n").split("\t", 1)
        counts[name] = counts.get(name, 0) + 1; max_idx = max(max_idx, int(i))
    # top each monster up to `target` references (front/back/side frames) without duplicating
    # the ones already stored.
    todo = []
    for lbl, paths in new_classes.items():
        target = len(paths) if target_refs <= 0 else target_refs
        have = counts.get(lbl, 0)
        for p in paths[have:target]:
            todo.append((lbl, p))
    if not todo:
        log("[append] every monster already has the requested references -> nothing to add."); return

    sess = ort.InferenceSession(onnx, providers=["CPUExecutionProvider"])
    in_name = sess.get_inputs()[0].name
    # back up once
    for p in (binp, labp, metap):
        if not os.path.exists(p + ".bak"): shutil.copy(p, p + ".bak")

    vecs, labels_added = [], []
    idx = max_idx
    for lbl, p in todo:
        try:
            x = preprocess(Image.open(p), img)[None, :, :, :].astype("float32")
            e = sess.run(None, {in_name: x})[0].reshape(-1).astype("float32")
            n = float(np.sqrt(max(np.dot(e, e), 1e-12)))
            e = e / n
            idx += 1
            vecs.append(e); labels_added.append((idx, lbl))
        except Exception as ex:
            log("  skip %s (%s)" % (lbl, ex))
    if not vecs:
        log("[append] nothing embedded."); return
    arr = np.ascontiguousarray(np.stack(vecs), dtype="float32")
    with open(binp, "ab") as f: f.write(arr.tobytes())
    with open(labp, "a", encoding="utf-8") as f:
        for i, lbl in labels_added: f.write("%d\t%s\n" % (i, lbl))
    meta["n"] = int(meta.get("n", 0)) + len(vecs)
    json.dump(meta, open(metap, "w"))
    # keep the shipped copy under src/.../models/icons and the build copy under icons/ in sync if present
    icons_lab = os.path.join(HERE, "icons", "labels.txt")
    log("[append] added %d monster references -> bank now n=%d" % (len(vecs), meta["n"]))

# ----------------------------------------------------------------- ship copies
SRC_MODELS = os.path.normpath(os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models"))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ICON_FILES = ["icon_refs.bin", "labels.txt", "icon_meta.json", "icon_embedder.onnx"]

def sync_runtime_copies():
    """Copy the updated icon bank into every built <app>/OcrServer/models/icons so the running
    app sees it without a rebuild. Best-effort over all bin/ outputs."""
    srcs = {f: os.path.join(OUT_DIR, f) for f in ICON_FILES if os.path.exists(os.path.join(OUT_DIR, f))}
    if not srcs: return
    targets = set()
    for pat in ("src/**/bin/**/OcrServer/models/icons", "src/**/bin/**/models/icons"):
        for d in glob.glob(os.path.join(REPO, pat), recursive=True):
            targets.add(d)
    n = 0
    for d in targets:
        try:
            os.makedirs(d, exist_ok=True)
            for f, sp in srcs.items(): shutil.copy(sp, os.path.join(d, f))
            n += 1
        except Exception:
            pass
    log("[sync] refreshed icon bank in %d built output folder(s)." % n)

def regen_vision_pack():
    """Rebuild the optional vision pack zip (runtime models only) at repo root."""
    import zipfile
    want = {
        "v5": ["ch_PP-OCRv5_mobile_det.onnx", "ch_ppocr_mobile_v2.0_cls_infer.onnx",
               "latin_PP-OCRv5_rec_mobile_infer.onnx", "ppocrv5_latin_dict.txt"],
        "icons": ["icon_embedder.onnx", "icon_refs.bin", "labels.txt", "icon_meta.json", "map_names.json"],
        "yolo": ["entity.onnx", "entity_meta.json"],
    }
    out = os.path.join(REPO, "4ViviTools-VisionPack.zip")
    try:
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
            for sub, files in want.items():
                for f in files:
                    sp = os.path.join(SRC_MODELS, sub, f)
                    if os.path.exists(sp): z.write(sp, arcname=os.path.join("models", sub, f))
        log("[pack] regenerated optional vision pack -> %s" % os.path.relpath(out, REPO))
    except Exception as e:
        log("[pack] could not regenerate pack: %s" % e)

def write_monster_manifest(src, grouped):
    manifest = {
        "source": src,
        "classes": [
            {"label": label, "frames": len(paths)}
            for label, paths in sorted(grouped.items())
        ],
        "class_count": len(grouped),
        "frame_count": sum(len(v) for v in grouped.values()),
    }
    path = os.path.join(ICONS, "monster_manifest.json")
    os.makedirs(ICONS, exist_ok=True)
    json.dump(manifest, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    log("[manifest] wrote %d monster classes / %d frames -> %s"
        % (manifest["class_count"], manifest["frame_count"], os.path.relpath(path, HERE)))

def quarantine_stale_mob_dirs(valid_labels):
    valid = {x.lower() for x in valid_labels}
    stale = []
    for d in glob.glob(os.path.join(RAW, "mob__*")):
        if os.path.isdir(d) and os.path.basename(d).lower() not in valid:
            stale.append(d)
    if not stale:
        return
    backup = os.path.join(ICONS, "stale_mob_backup")
    os.makedirs(backup, exist_ok=True)
    moved = 0
    for d in stale:
        name = os.path.basename(d)
        target = os.path.join(backup, name)
        if os.path.exists(target):
            target = os.path.join(backup, "%s_%d" % (name, moved + 1))
        try:
            shutil.move(d, target)
            moved += 1
        except Exception as e:
            log("[cleanup] could not move stale mob dir %s (%s)" % (name, e))
    log("[cleanup] quarantined %d stale mob__ folders -> %s" % (moved, os.path.relpath(backup, HERE)))

def monster_label_from_sprite_name(name):
    raw = name
    if "__" in raw:
        prefix, suffix = raw.split("__", 1)
        if any(not (ch.isascii() and (ch.isalnum() or ch == "_")) for ch in suffix):
            raw = prefix
    else:
        kept = []
        for ch in raw:
            if ch.isascii() and (ch.isalnum() or ch == "_"):
                kept.append(ch)
            else:
                break
        trimmed = "".join(kept).rstrip("_")
        if trimmed:
            raw = trimmed
    safe = "".join(ch if ch.isascii() and (ch.isalnum() or ch == "_") else "_" for ch in raw).strip("_")
    while "__" in safe:
        safe = safe.replace("__", "_")
    return "mob__" + safe if safe else ""

# ----------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="", help="folder with monster .spr/.act pairs")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--refs", type=int, default=0, help="distinct frames/poses to keep per monster; 0 = all distinct frames")
    ap.add_argument("--force", action="store_true", help="rewrite existing mob__ PNG frames from the GRF")
    ap.add_argument("--no-append", action="store_true", help="only render PNGs, don't touch the bank")
    ap.add_argument("--manifest-only", action="store_true", help="write monster_manifest.json from existing mob__ PNG folders without decoding sprites")
    a = ap.parse_args()
    src_arg = a.src or default_src()
    src = src_arg if os.path.isabs(src_arg) else os.path.abspath(src_arg)
    log("source: " + src)
    if not os.path.isdir(src):
        log("ERROR: folder not found."); return
    sprs = sorted(glob.glob(os.path.join(src, "**", "*.spr"), recursive=True))
    if a.limit: sprs = sprs[: a.limit]
    log("found %d .spr files" % len(sprs))
    os.makedirs(RAW, exist_ok=True)

    if a.manifest_only:
        grouped = {}
        for sp in sprs:
            name = os.path.splitext(os.path.basename(sp))[0]
            label = monster_label_from_sprite_name(name)
            if not label:
                continue
            paths = sorted(glob.glob(os.path.join(RAW, label, "*.png")))
            if paths:
                grouped[label] = paths
        write_monster_manifest(src, grouped)
        log("DONE.")
        return

    grouped = {}
    ok = fail = 0
    for sp in sprs:
        name = os.path.splitext(os.path.basename(sp))[0]
        act = os.path.splitext(sp)[0] + ".act"
        label = monster_label_from_sprite_name(name)
        if not label:
            fail += 1
            continue
        d = os.path.join(RAW, label); os.makedirs(d, exist_ok=True)
        if a.force:
            for old in glob.glob(os.path.join(d, "*.png")):
                try: os.remove(old)
                except OSError: pass
        paths = []
        # primary clean idle pose -> 0.png (composited via the .act)
        p0 = os.path.join(d, "0.png")
        if not os.path.exists(p0):
            try:
                img = render_from_act(sp, act if os.path.exists(act) else "")
            except Exception:
                img = None
            if img is not None and img.width >= 4 and img.height >= 4:
                img.save(p0)
        if os.path.exists(p0): paths.append(p0)
        # extra DISTINCT poses (the monster turning: back / sides) -> 1.png .. k.png
        try:
            extras = extra_frames(sp, k=0 if a.refs <= 0 else max(0, a.refs - 1))
        except Exception:
            extras = []
        ei = 1
        for ex in extras:
            pe = os.path.join(d, "%d.png" % ei)
            if not os.path.exists(pe):
                try: ex.convert("RGBA").save(pe)
                except Exception: pass
            if os.path.exists(pe): paths.append(pe); ei += 1
        if paths:
            grouped[label] = paths; ok += 1
        else:
            fail += 1
        if ok and ok % 100 == 0: log("  rendered %d monsters ..." % ok)
    total_frames = sum(len(v) for v in grouped.values())
    pose_text = "all distinct" if a.refs <= 0 else "up to %d" % a.refs
    log("rendered %d monsters, %d total frames (%d failed) -> icons/raw/mob__* (%s poses each)"
        % (len(grouped), total_frames, fail, pose_text))
    write_monster_manifest(src, grouped)
    if not a.limit:
        quarantine_stale_mob_dirs(grouped.keys())

    if not a.no_append:
        append_to_bank(grouped, target_refs=a.refs)
        sync_runtime_copies()
        regen_vision_pack()
    log("DONE.")

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc(); sys.exit(1)
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
