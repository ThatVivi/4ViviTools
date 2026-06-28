#!/usr/bin/env python3
"""
train_yolo.py -- train the ENTITY detector (YOLO11n) on the merged real Roboflow datasets, export to ONNX.

Detector = WHERE entities are on screen (single class "entity"); the icon embedder then says WHICH.
Self-contained: installs ultralytics (+ a CUDA torch if possible), trains/resumes, exports ONNX
straight to src/RapidOcrNet/models/yolo/entity.onnx (no paddle2onnx needed - ultralytics exports
ONNX directly). Resumable: re-run to continue. Flags: --epochs 60 --imgsz 640 --cpu
"""
import argparse, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "yolo_real", "data.yaml")   # merged real Roboflow datasets
RUNS = os.path.join(HERE, "yolo_real", "runs")
OUT_DIR = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "yolo")

def log(m): print(m, flush=True)
def pip(*a): subprocess.call([sys.executable, "-m", "pip", *a])

def cuda_ok():
    """Check (in a SEPARATE process, so we don't lock a CPU torch into this one) whether the
    installed torch can actually use the GPU."""
    r = subprocess.run([sys.executable, "-c", "import torch; print(torch.cuda.is_available())"],
                       capture_output=True, text=True)
    return "True" in (r.stdout or "")

def ensure_cuda_torch():
    """Force a CUDA (cu124) torch build if the current one is CPU-only. Matches the RTX 2060
    (compute 7.5) + the installed CUDA 12.6 runtime. Safe: only runs when GPU is wanted and the
    current torch can't see the GPU."""
    if cuda_ok():
        return
    log("[yolo] current torch is CPU-only -> installing CUDA (cu124) torch+torchvision...")
    pip("install", "--force-reinstall", "--no-cache-dir", "torch", "torchvision",
        "--index-url", "https://download.pytorch.org/whl/cu124")


def ensure_ultralytics():
    try:
        import ultralytics  # noqa
        return
    except Exception:
        pip("install", "--no-cache-dir", "ultralytics")

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--epochs", type=int, default=40)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--cpu", action="store_true")
    a = ap.parse_args()
    if not os.path.exists(DATA):
        log("[yolo] no data.yaml -> run merge_datasets.py first"); return
    want_gpu = not a.cpu
    if want_gpu:
        ensure_cuda_torch()           # force a CUDA torch if needed (before torch is imported here)
    ensure_ultralytics()
    import torch                       # first torch import in this process -> picks up the GPU build
    use_gpu = want_gpu and torch.cuda.is_available()
    log("[yolo] device: %s%s" % ("GPU" if use_gpu else "CPU",
        "" if use_gpu else " (no CUDA torch; run again or pass nothing to retry GPU install)"))

    from ultralytics import YOLO
    last = os.path.join(RUNS, "entity", "weights", "last.pt")
    if os.path.exists(last):
        log("[yolo] resuming from %s" % last)
        model = YOLO(last)
        model.train(resume=True)
    else:
        pre = os.path.join(HERE, "yolo11n.pt")   # pre-fetched weights (avoids the broken auto-download)
        model = YOLO(pre if os.path.exists(pre) else "yolo11n.pt")   # guide §8: YOLO11n
        model.train(data=DATA, epochs=a.epochs, imgsz=a.imgsz, batch=16,
                     device=(0 if use_gpu else "cpu"), project=RUNS, name="entity",
                     exist_ok=True, patience=20, verbose=True)

    best = os.path.join(RUNS, "entity", "weights", "best.pt")
    if not os.path.exists(best): best = last
    if not os.path.exists(best):
        log("[yolo] no trained weights found -> abort export"); return
    log("[yolo] exporting ONNX from %s" % best)
    m = YOLO(best)
    onnx = m.export(format="onnx", opset=12, imgsz=a.imgsz, simplify=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    dst = os.path.join(OUT_DIR, "entity.onnx")
    shutil.copy(str(onnx), dst)
    import json as _json, yaml as _yaml
    try:
        _names = _yaml.safe_load(open(DATA, encoding="utf-8")).get("names") or ["entity"]
    except Exception:
        _names = ["entity"]
    open(os.path.join(OUT_DIR, "entity_meta.json"), "w").write(
        _json.dumps({"imgsz": a.imgsz, "classes": _names, "format": "yolo11"}))
    log("[yolo] DONE -> %s (%d KB)" % (os.path.relpath(dst, HERE), os.path.getsize(dst)//1024))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
