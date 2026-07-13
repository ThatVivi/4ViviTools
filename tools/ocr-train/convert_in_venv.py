#!/usr/bin/env python3
"""
convert_in_venv.py -- run INSIDE the clean ocr_export venv (launched by convert_in_venv.bat).

Converts the trained models to ONNX in an isolated env, away from the polluted global
site-packages (vllm/opentelemetry/paddlex keep breaking paddle2onnx there).

  TEXT  : A) paddle2onnx 2.0.2rc3 on the existing PIR model (work/inference_rec/inference.json)
          B) fallback: paddle 3.1.0 legacy re-export from best_accuracy, then convert
  ICONS : C) build MobileNetV3-Large embedder, load trained weights, export icon_embedder.onnx
Installs results into src/RapidOcrNet/models/.
"""
import os, sys, glob, shutil, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.join(HERE, "work")
PIR = os.path.join(WORK, "inference_rec")
SAVE = os.path.join(WORK, "output", "rec_ro")
DICT = os.path.join(HERE, "ppocrv5_latin_dict.txt")
DST = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "v5",
                   "latin_PP-OCRv5_rec_mobile_infer.onnx")
ICON_DIR = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "icons")

def log(m): print(m, flush=True)
def pip(*a): subprocess.check_call([sys.executable, "-m", "pip", *a])
def fwd(p): return p.replace("\\", "/")

def ensure_p2o():
    try:
        import paddle2onnx  # noqa
        return
    except Exception:
        pass
    pip("install", "--no-cache-dir", "setuptools", "wheel", "packaging",
        "protobuf==4.25.8", "onnx==1.17.0", "paddle2onnx==2.0.2rc3", "paddlepaddle==3.1.0")
    import importlib; importlib.invalidate_caches()
    import paddle2onnx  # noqa

def ensure_paddle():
    try:
        import paddle  # noqa
        return
    except Exception:
        pip("install", "--no-cache-dir", "setuptools", "wheel", "paddlepaddle==3.1.0")

def install(onnx):
    if not (os.path.exists(onnx) and os.path.getsize(onnx) > 1024):
        raise RuntimeError("no valid ONNX produced")
    if os.path.exists(DST) and not os.path.exists(DST + ".bak"):
        shutil.copy(DST, DST + ".bak")
    shutil.copy(onnx, DST)
    log("DONE -> text model installed (%d KB): %s" % (os.path.getsize(DST)//1024, os.path.relpath(DST, HERE)))

def attempt_A():
    log("[A] convert existing PIR model with paddle2onnx 2.0.2rc3")
    ensure_p2o()
    import paddle2onnx
    mf = os.path.join(PIR, "inference.json"); pf = os.path.join(PIR, "inference.pdiparams")
    onnx = os.path.join(WORK, "rec.onnx")
    if not os.path.exists(mf):
        log("    no inference.json -> skip A"); return None
    if os.path.exists(onnx): os.remove(onnx)
    try:
        paddle2onnx.export(mf, pf, save_file=onnx, opset_version=11)
    except TypeError:
        data = paddle2onnx.export(model_filename=mf, params_filename=pf, opset_version=11)
        if isinstance(data, (bytes, bytearray)): open(onnx, "wb").write(data)
    return onnx if (os.path.exists(onnx) and os.path.getsize(onnx) > 1024) else None

def attempt_B():
    log("[B] re-export LEGACY with paddlepaddle 3.1.0, then convert")
    pip("install", "--no-cache-dir", "paddlepaddle==3.1.0", "pyyaml", "opencv-python-headless",
        "shapely", "pyclipper", "scikit-image", "lmdb", "tqdm", "rapidfuzz", "albumentations", "Pillow")
    repo = os.path.join(HERE, "PaddleOCR"); exp = os.path.join(repo, "tools", "export_model.py")
    if not os.path.exists(exp):
        log("    PaddleOCR repo missing -> cannot do B"); return None
    sys.path.insert(0, HERE)
    import train_export
    cfg = train_export.find_rec_config(repo)
    best = os.path.join(SAVE, "best_accuracy")
    if not os.path.exists(best + ".pdparams"): best = os.path.join(SAVE, "latest")
    out = os.path.join(WORK, "infer_legacy_31"); shutil.rmtree(out, ignore_errors=True)
    subprocess.check_call([sys.executable, exp, "-c", cfg, "-o",
                           f"Global.pretrained_model={fwd(best)}",
                           f"Global.character_dict_path={fwd(DICT)}",
                           f"Global.save_inference_dir={fwd(out)}",
                           "Global.export_with_pir=False"])
    mf = os.path.join(out, "inference.pdmodel")
    if not os.path.exists(mf):
        log("    legacy export produced no inference.pdmodel"); return None
    import paddle2onnx
    onnx = os.path.join(WORK, "rec.onnx")
    if os.path.exists(onnx): os.remove(onnx)
    paddle2onnx.export(mf, os.path.join(out, "inference.pdiparams"), save_file=onnx, opset_version=11)
    return onnx if (os.path.exists(onnx) and os.path.getsize(onnx) > 1024) else None

def convert_icon_embedder():
    """Export the trained icon EMBEDDER (MobileNetV3-Large -> N-D vector) to ONNX, in the clean venv.
    Mirrors the text path: jit.save the dygraph net, then paddle2onnx.export the result."""
    meta_p = os.path.join(ICON_DIR, "icon_meta.json")
    wts = os.path.join(ICON_DIR, "icon_embedder.pdparams")
    if not (os.path.exists(meta_p) and os.path.exists(wts)):
        log("[C] no trained icon embedder yet -> skip (run build_icon_model.py first)"); return
    log("[C] export icon embedder -> ONNX")
    ensure_paddle(); ensure_p2o()
    import json
    meta = json.load(open(meta_p)); emb = int(meta.get("emb", 256)); img = int(meta.get("img", 64))
    import paddle, paddle2onnx
    from paddle.vision.models import mobilenet_v3_large
    net = mobilenet_v3_large(num_classes=emb); net.set_state_dict(paddle.load(wts)); net.eval()
    wd = os.path.join(WORK, "icon_infer"); shutil.rmtree(wd, ignore_errors=True); os.makedirs(wd, exist_ok=True)
    prefix = os.path.join(wd, "inference")
    paddle.jit.save(net, prefix, input_spec=[paddle.static.InputSpec([None, 3, img, img], "float32", "x")])
    mf = prefix + ".json" if os.path.exists(prefix + ".json") else prefix + ".pdmodel"
    onnx = os.path.join(ICON_DIR, "icon_embedder.onnx")
    if os.path.exists(onnx): os.remove(onnx)
    paddle2onnx.export(mf, prefix + ".pdiparams", save_file=onnx, opset_version=11)
    if os.path.exists(onnx) and os.path.getsize(onnx) > 1024:
        log("[C] DONE -> icon embedder ONNX (%d KB): %s" % (os.path.getsize(onnx)//1024, os.path.relpath(onnx, HERE)))
    else:
        log("[C] icon embedder ONNX not produced")

def convert_text():
    for fn in (attempt_A, attempt_B):
        try:
            onnx = fn()
            if onnx:
                install(onnx); return True
        except Exception:
            import traceback; traceback.print_exc()
        log("    -> that path failed, trying next")
    log("text conversion: both paths failed (see above)")
    return False

def main():
    convert_text()
    try:
        convert_icon_embedder()   # runs regardless of text result
    except Exception:
        import traceback; traceback.print_exc()

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
