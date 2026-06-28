#!/usr/bin/env python3
"""
build_map_names.py -- add MAP minimaps to the icon recognizer (no retrain; metric-learning bank append).

Maps are flat images (GRF pre-rendered minimaps: data/texture/유저인터페이스/map/*.bmp). This:
  1) copies each map image -> icons/raw/map__<name>/0.png
  2) embeds it with icon_embedder.onnx and APPENDS to the bank (icon_refs.bin + labels.txt + icon_meta.json)
  3) syncs the bank into built OcrServer/models/icons outputs and regenerates the vision pack
Names already in the bank are skipped, so re-running only adds new maps.

>>> SET THIS to the folder that holds your extracted map .bmp/.png files <<<
Default points at the project GRF minimap folder; change DEFAULT_SRC or pass --src "C:\\path\\to\\maps".
"""
import argparse, glob, os, sys, subprocess, json, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "icons", "raw")
OUT_DIR = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "icons")
SRC_MODELS = os.path.normpath(os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models"))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ICON_FILES = ["icon_refs.bin", "labels.txt", "icon_meta.json", "icon_embedder.onnx", "map_names.json"]

# ===================== EDIT ME (or pass --src) =====================
DEFAULT_SRCS = [
    os.path.join(HERE, "..", "..", "GRF", "kRO Data", "data", "texture", "유저인터페이스", "map"),
    os.path.join(HERE, "..", "..", "GRF", "data", "texture", "유저인터페이스", "map"),
]
# mapnametable.txt: internal.rsw#DisplayName# -> ships as map_names.json (internal -> display)
NAMES_FILE = os.path.join(HERE, "..", "..", "GRF", "data", "mapnametable.txt")
# ==================================================================

def log(m): print(m, flush=True)
def ensure(mod, pip_name=None):
    try: return __import__(mod)
    except Exception:
        subprocess.call([sys.executable, "-m", "pip", "install", "--quiet", "--break-system-packages", pip_name or mod])
        return __import__(mod)

PIL = ensure("PIL", "pillow"); from PIL import Image
np = ensure("numpy")

def preprocess(img, size):
    im = img.convert("RGB").resize((size, size), Image.LANCZOS)
    return ((np.asarray(im, "float32") / 255.0 - 0.5) / 0.5).transpose(2, 0, 1)

def append_to_bank(new_classes):
    onnx = os.path.join(OUT_DIR, "icon_embedder.onnx"); binp = os.path.join(OUT_DIR, "icon_refs.bin")
    labp = os.path.join(OUT_DIR, "labels.txt"); metap = os.path.join(OUT_DIR, "icon_meta.json")
    if not all(os.path.exists(x) for x in (onnx, binp, labp, metap)):
        log("[append] bank/embedder missing in %s -> skipping live append. Run build_icon_model.py to fold in." % OUT_DIR); return
    ort = ensure("onnxruntime")
    meta = json.load(open(metap)); emb, img = int(meta.get("emb", 256)), int(meta.get("img", 64))
    existing = set(); max_idx = -1
    for line in open(labp, encoding="utf-8"):
        if "\t" not in line: continue
        i, name = line.rstrip("\n").split("\t", 1); existing.add(name); max_idx = max(max_idx, int(i))
    todo = [(l, p) for (l, p) in new_classes if l not in existing]
    if not todo: log("[append] all %d maps already in the bank." % len(new_classes)); return
    sess = ort.InferenceSession(onnx, providers=["CPUExecutionProvider"]); inn = sess.get_inputs()[0].name
    for p in (binp, labp, metap):
        if not os.path.exists(p + ".bak"): shutil.copy(p, p + ".bak")
    vecs, labels_added = [], []; idx = max_idx
    for lbl, p in todo:
        try:
            x = preprocess(Image.open(p), img)[None].astype("float32")
            e = sess.run(None, {inn: x})[0].reshape(-1).astype("float32")
            e = e / float(np.sqrt(max(np.dot(e, e), 1e-12))); idx += 1
            vecs.append(e); labels_added.append((idx, lbl))
        except Exception as ex: log("  skip %s (%s)" % (lbl, ex))
    if not vecs: return
    with open(binp, "ab") as f: f.write(np.ascontiguousarray(np.stack(vecs), "float32").tobytes())
    with open(labp, "a", encoding="utf-8") as f:
        for i, lbl in labels_added: f.write("%d\t%s\n" % (i, lbl))
    meta["n"] = int(meta.get("n", 0)) + len(vecs); json.dump(meta, open(metap, "w"))
    log("[append] added %d map references -> bank now n=%d" % (len(vecs), meta["n"]))

def sync_runtime_copies():
    srcs = {f: os.path.join(OUT_DIR, f) for f in ICON_FILES if os.path.exists(os.path.join(OUT_DIR, f))}
    if not srcs: return
    targets = set()
    for pat in ("src/**/bin/**/OcrServer/models/icons", "src/**/bin/**/models/icons"):
        targets.update(glob.glob(os.path.join(REPO, pat), recursive=True))
    n = 0
    for d in targets:
        try:
            os.makedirs(d, exist_ok=True)
            for f, sp in srcs.items(): shutil.copy(sp, os.path.join(d, f))
            n += 1
        except Exception: pass
    log("[sync] refreshed icon bank in %d built output folder(s)." % n)

def regen_vision_pack():
    import zipfile
    want = {"v5": ["ch_PP-OCRv5_mobile_det.onnx", "ch_ppocr_mobile_v2.0_cls_infer.onnx", "latin_PP-OCRv5_rec_mobile_infer.onnx", "ppocrv5_latin_dict.txt"],
            "icons": ["icon_embedder.onnx", "icon_refs.bin", "labels.txt", "icon_meta.json", "map_names.json"],
            "yolo": ["entity.onnx", "entity_meta.json"]}
    out = os.path.join(REPO, "4ViviTools-VisionPack.zip")
    try:
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
            for sub, files in want.items():
                for f in files:
                    sp = os.path.join(SRC_MODELS, sub, f)
                    if os.path.exists(sp): z.write(sp, arcname=os.path.join("models", sub, f))
        log("[pack] regenerated vision pack -> %s" % os.path.relpath(out, REPO))
    except Exception as e: log("[pack] could not regenerate pack: %s" % e)

def load_map_display(path):
    d = {}
    if not os.path.isfile(path): return d
    for enc in ("cp949", "utf-8", "latin-1"):
        try:
            cur = {}
            for line in open(path, encoding=enc, errors="ignore"):
                line = line.strip()
                if not line or line.startswith("//") or "#" not in line: continue
                left, rest = line.split("#", 1)
                internal = os.path.splitext(left.strip())[0].lower()
                disp = rest.split("#", 1)[0].strip()
                if internal and disp: cur[internal] = disp
            if cur: return cur
        except Exception:
            continue
    return d

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="", help="folder with map .bmp/.png minimaps (default: both GRF map folders)")
    ap.add_argument("--names", default=NAMES_FILE, help="mapnametable.txt (internal.rsw#Display#)")
    ap.add_argument("--no-append", action="store_true")
    a = ap.parse_args()
    srcs = [a.src] if a.src else DEFAULT_SRCS
    srcs = [d if os.path.isabs(d) else os.path.abspath(d) for d in srcs]
    by_name = {}   # basename(lower) -> path, first dir wins
    for d in srcs:
        if not os.path.isdir(d):
            log("  (skip, not found) " + d); continue
        log("source: " + d)
        for ext in ("*.bmp", "*.png", "*.jpg", "*.tga"):
            for ip in glob.glob(os.path.join(d, "**", ext), recursive=True):
                key = os.path.splitext(os.path.basename(ip))[0].lower()
                by_name.setdefault(key, ip)
    imgs = [by_name[k] for k in sorted(by_name)]
    if not imgs:
        log("ERROR: no map images found. Pass --src with your extracted maps folder."); return
    log("found %d unique map images across %d folder(s)" % (len(imgs), len(srcs)))
    os.makedirs(RAW, exist_ok=True)
    rendered = []; ok = fail = 0
    for ip in imgs:
        name = os.path.splitext(os.path.basename(ip))[0]
        label = "map__" + "".join(ch if ch.isalnum() or ch in "_@" else "_" for ch in name)
        d = os.path.join(RAW, label); outp = os.path.join(d, "0.png")
        if os.path.exists(outp): rendered.append((label, outp)); ok += 1; continue
        try:
            os.makedirs(d, exist_ok=True); Image.open(ip).convert("RGBA").save(outp)
            rendered.append((label, outp)); ok += 1
        except Exception as e: fail += 1; log("  skip %s (%s)" % (name, e))
    log("prepared %d maps (%d failed) -> icons/raw/map__*" % (ok, fail))
    # emit internal -> display name table from mapnametable.txt so maps carry real names
    disp = load_map_display(a.names if os.path.isabs(a.names) else os.path.abspath(a.names))
    if disp:
        try:
            os.makedirs(OUT_DIR, exist_ok=True)
            json.dump(disp, open(os.path.join(OUT_DIR, "map_names.json"), "w", encoding="utf-8"), ensure_ascii=False)
            log("[names] wrote %d map display names -> map_names.json" % len(disp))
        except Exception as e:
            log("[names] could not write map_names.json: %s" % e)
    else:
        log("[names] mapnametable.txt not found at %s -> labels stay internal-only." % a.names)
    if not a.no_append:
        append_to_bank(rendered); sync_runtime_copies(); regen_vision_pack()
    log("DONE.")

if __name__ == "__main__":
    try: main()
    except Exception: import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
