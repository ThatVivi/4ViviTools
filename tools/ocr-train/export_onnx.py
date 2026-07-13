#!/usr/bin/env python3
"""
export_onnx.py -- finish the ONNX export that paddle2onnx couldn't do during train_all.

Two problems are handled here:
  1) protobuf 7.x was too new for paddle2onnx's compiled extension -> pin a compatible protobuf.
  2) paddle 3.2.1 exports the NEW PIR format (inference.json) which paddle2onnx 2.1.0 can't parse.
     -> we RE-EXPORT the trained model with PIR disabled (legacy inference.pdmodel), then convert.

No retraining. Double-click to run. Your previous app model is backed up as *.onnx.bak.
"""
import os, sys, glob, shutil, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.join(HERE, "work")
PIR_INFER = os.path.join(WORK, "inference_rec")        # PIR model train_all already exported (inference.json)
SAVE = os.path.join(WORK, "output", "rec_ro")
DICT = os.path.join(HERE, "ppocrv5_latin_dict.txt")
DST = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "v5",
                   "latin_PP-OCRv5_rec_mobile_infer.onnx")

COMBOS = [("5.29.5", "2.1.0"), ("4.25.8", "2.1.0"), ("5.29.5", "2.0.2rc3"), ("4.25.8", "2.0.2rc3")]

def log(m): print(m, flush=True)
def pip(*a): subprocess.call([sys.executable, "-m", "pip", *a])
def fwd(p): return p.replace("\\", "/")

def _imports_ok():
    r = subprocess.run([sys.executable, "-c", "import paddle2onnx, paddle; print('IMPORTS_OK')"],
                       capture_output=True, text=True)
    return "IMPORTS_OK" in (r.stdout or "")

def repair():
    if _imports_ok():
        log("    paddle + paddle2onnx already import OK"); return True
    for proto, p2o in COMBOS:
        log("    trying protobuf==%s + paddle2onnx==%s" % (proto, p2o))
        pip("install", "--force-reinstall", "--no-cache-dir", "protobuf==%s" % proto)
        pip("uninstall", "-y", "paddle2onnx")
        pip("install", "--no-cache-dir", "paddle2onnx==%s" % p2o)
        if _imports_ok():
            log("    OK -> protobuf %s + paddle2onnx %s" % (proto, p2o)); return True
    return False

def find_repo():
    repo = os.path.join(HERE, "PaddleOCR")
    return repo if os.path.exists(os.path.join(repo, "tools", "export_model.py")) else None

def ensure_pir_model():
    """The PIR inference model (inference.json + .pdiparams) from train_all. Re-export if missing."""
    if os.path.exists(os.path.join(PIR_INFER, "inference.json")):
        return True
    sys.path.insert(0, HERE)
    import train_export
    repo = find_repo()
    if not repo:
        log("    PaddleOCR repo missing -> run run.py first"); return False
    cfg = train_export.find_rec_config(repo)
    best = os.path.join(SAVE, "best_accuracy")
    if not os.path.exists(best + ".pdparams"):
        best = os.path.join(SAVE, "latest")
    subprocess.check_call([sys.executable, os.path.join(repo, "tools", "export_model.py"),
                           "-c", cfg, "-o",
                           f"Global.pretrained_model={fwd(best)}",
                           f"Global.character_dict_path={fwd(DICT)}",
                           f"Global.save_inference_dir={fwd(PIR_INFER)}"])
    return os.path.exists(os.path.join(PIR_INFER, "inference.json"))

def convert():
    """PaddleX's paddle2onnx plugin is the PIR-aware converter PaddleOCR 3.x uses for the new
    inference.json format (plain paddle2onnx 2.1.0 can't parse it; the legacy .pdmodel export
    is broken on paddle 3.2.1). It reads PIR_INFER and writes <out_dir>/inference.onnx."""
    out_dir = os.path.join(WORK, "onnx"); shutil.rmtree(out_dir, ignore_errors=True)
    # 1) make sure PaddleX + its paddle2onnx plugin are present
    pip("install", "-U", "--no-cache-dir", "paddlex")
    subprocess.call([sys.executable, "-m", "paddlex", "--install", "paddle2onnx"])
    # PaddleX's plugin install pulls paddle2onnx==2.0.2rc3 but leaves protobuf 7.x, which breaks
    # the plugin's compiled extension ("Paddle2ONNX is not available"). Pin a compatible protobuf.
    for proto in ("4.25.8", "5.29.5"):
        pip("install", "--force-reinstall", "--no-cache-dir", "protobuf==%s" % proto)
        if _imports_ok():
            log("    protobuf pinned to %s (plugin imports OK)" % proto); break
    # 2) convert PIR model -> ONNX
    subprocess.check_call([sys.executable, "-m", "paddlex", "--paddle2onnx",
                           "--paddle_model_dir", PIR_INFER,
                           "--onnx_model_dir", out_dir,
                           "--opset_version", "11"])
    cands = [os.path.join(out_dir, "inference.onnx")] + glob.glob(os.path.join(out_dir, "*.onnx"))
    for onnx in cands:
        if os.path.exists(onnx) and os.path.getsize(onnx) > 1024:
            return onnx
    raise RuntimeError("PaddleX paddle2onnx produced no valid ONNX (see messages above)")

def main():
    log("[1/4] repair paddle2onnx + protobuf")
    if not repair():
        log("    couldn't get paddle + paddle2onnx to import together.")
        log('    run: python -c "import paddle2onnx"  and paste me the full error.')
        return
    log("[2/4] ensure PIR inference model exists")
    if not ensure_pir_model():
        log("    no PIR inference model and could not rebuild it."); return
    log("[3/4] convert to ONNX (paddle.onnx.export, in-memory PIR)")
    onnx = convert()
    log("[4/4] install -> models/v5")
    if os.path.exists(DST) and not os.path.exists(DST + ".bak"):
        shutil.copy(DST, DST + ".bak")
    shutil.copy(onnx, DST)
    log("DONE -> text OCR model installed (%d KB): %s"
        % (os.path.getsize(DST) // 1024, os.path.relpath(DST, HERE)))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
