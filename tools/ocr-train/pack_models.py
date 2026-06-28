#!/usr/bin/env python3
"""
pack_models.py -- zip the trained models into an OPTIONAL download pack.

Base app ships with NO models -> recognizers self-disable (Available=false), app still runs.
User downloads 4rvivi_models_pack.zip, extracts into the app's `models/` folder, and text OCR +
icon recognition + entity detection turn on automatically. Double-click to build the pack.
"""
import os, sys, json, hashlib, zipfile, datetime

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models")
OUT = os.path.join(HERE, "..", "..", "dist", "4rvivi_models_pack.zip")

# files that make up the optional pack (relative to models/)
WANT = [
    "v5/latin_PP-OCRv5_rec_mobile_infer.onnx",
    "v5/ppocrv5_latin_dict.txt",
    "icons/icon_embedder.onnx",
    "icons/icon_refs.bin",
    "icons/labels.txt",
    "icons/icon_meta.json",
    "yolo/entity.onnx",
    "yolo/entity_meta.json",
]

def sha(p, n=16):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()[:n]

def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    files, manifest, total = [], [], 0
    for rel in WANT:
        p = os.path.join(MODELS, *rel.split("/"))
        if os.path.exists(p) and os.path.getsize(p) > 0:
            sz = os.path.getsize(p); total += sz
            files.append((p, "models/" + rel))
            manifest.append({"path": "models/" + rel, "bytes": sz, "sha256_16": sha(p)})
            print("  + %-46s %7d KB" % (rel, sz // 1024))
        else:
            print("  - MISSING (skipped): %s" % rel)
    if not files:
        print("no model files found -> run build_all.bat first"); return
    meta = {"name": "4rViviTools models pack", "built": datetime.datetime.now().isoformat(timespec="seconds"),
            "total_bytes": total, "files": manifest,
            "install": "Extract into the app folder so files land under models/ next to the .exe."}
    if os.path.exists(OUT): os.remove(OUT)
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for src, arc in files: z.write(src, arc)
        z.writestr("models/pack_manifest.json", json.dumps(meta, indent=2))
    print("\nDONE -> %s (%d files, %.1f MB)" % (os.path.relpath(OUT, HERE), len(files), os.path.getsize(OUT) / 1e6))

if __name__ == "__main__":
    try: main()
    except Exception: import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
