#!/usr/bin/env python3
"""
train_yolo.py -- train the ENTITY detector (YOLO11n) on the merged real Roboflow datasets, export to ONNX.

Detector = WHERE entities are on screen (single class "entity"); the icon embedder then says WHICH.
Self-contained: installs ultralytics (+ a CUDA torch if possible), trains/resumes, exports ONNX
straight to src/RapidOcrNet/models/yolo/entity.onnx (no paddle2onnx needed - ultralytics exports
ONNX directly). Resumable: re-run to continue. Flags: --epochs 60 --imgsz 640 --cpu
"""
import argparse, csv, json, os, shutil, subprocess, sys
from datetime import datetime

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


def newest_mtime(path):
    if not os.path.exists(path):
        return 0
    latest = os.path.getmtime(path)
    if os.path.isdir(path):
        for root, _, files in os.walk(path):
            for name in files:
                try:
                    latest = max(latest, os.path.getmtime(os.path.join(root, name)))
                except OSError:
                    pass
    return latest


def completed_epochs(run_dir):
    results = os.path.join(run_dir, "results.csv")
    if not os.path.isfile(results):
        return 0
    count = 0
    with open(results, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("epoch,"):
                count += 1
    return count


def planned_epochs(run_dir):
    args = os.path.join(run_dir, "args.yaml")
    if not os.path.isfile(args):
        return 0
    with open(args, encoding="utf-8", errors="replace") as f:
        for line in f:
            text = line.strip()
            if text.startswith("epochs:"):
                try:
                    return int(text.split(":", 1)[1].strip())
                except ValueError:
                    return 0
    return 0


def archive_stale_run(run_dir, reason):
    if not os.path.isdir(run_dir):
        return None
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    dst = os.path.join(RUNS, "entity_stale_" + stamp)
    log("[yolo] existing run is stale (%s); moving it to %s" % (reason, os.path.basename(dst)))
    shutil.move(run_dir, dst)
    return dst


def backup_existing_export(path):
    if not os.path.isfile(path):
        return
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    backup = path + ".bak_" + stamp
    shutil.copy2(path, backup)
    log("[yolo] backed up previous ONNX -> %s" % os.path.basename(backup))


def read_training_metrics(run_dir):
    results = os.path.join(run_dir, "results.csv")
    if not os.path.isfile(results):
        return {}

    with open(results, newline="", encoding="utf-8", errors="replace") as f:
        rows = list(csv.DictReader(f))
    if not rows:
        return {}

    last = rows[-1]
    metrics = {}
    for key, value in last.items():
        name = (key or "").strip()
        if not name:
            continue
        try:
            metrics[name] = float(str(value).strip())
        except ValueError:
            pass
    return metrics


def metric_value(metrics, *contains):
    for key, value in metrics.items():
        low = key.lower()
        if all(part.lower() in low for part in contains):
            return value
    return None


def validate_training_metrics(run_dir, min_map50, min_map5095):
    metrics = read_training_metrics(run_dir)
    report_path = os.path.join(run_dir, "4vivi_metrics_summary.json")
    if metrics:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(metrics, f, indent=2)
        log("[yolo] metrics summary -> %s" % os.path.relpath(report_path, HERE))

    map50 = metric_value(metrics, "map50")
    map5095 = metric_value(metrics, "map50-95") or metric_value(metrics, "map")
    if map50 is not None and map50 < min_map50:
        raise RuntimeError("[yolo] mAP50 %.3f is below safety floor %.3f; keeping previous export." % (map50, min_map50))
    if map5095 is not None and map5095 < min_map5095:
        raise RuntimeError("[yolo] mAP50-95 %.3f is below safety floor %.3f; keeping previous export." % (map5095, min_map5095))
    if map50 is not None or map5095 is not None:
        log("[yolo] metric guard OK: mAP50=%s mAP50-95=%s" % (
            "?" if map50 is None else f"{map50:.3f}",
            "?" if map5095 is None else f"{map5095:.3f}"))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--epochs", type=int, default=60)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--batch", type=int, default=16)
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--patience", type=int, default=18)
    ap.add_argument("--model", default="yolo11n.pt")
    ap.add_argument("--cache", default="ram", choices=("false", "ram", "disk"),
                    help="Ultralytics image cache mode. ram is fastest on 32GB systems.")
    ap.add_argument("--cpu", action="store_true")
    ap.add_argument("--fresh", action="store_true",
                    help="archive any existing yolo_real/runs/entity before training")
    ap.add_argument("--min-map50", type=float, default=0.70)
    ap.add_argument("--min-map5095", type=float, default=0.35)
    a = ap.parse_args()
    if not os.path.exists(DATA):
        raise RuntimeError("[yolo] no data.yaml -> run merge_datasets.py first")
    want_gpu = not a.cpu
    if want_gpu:
        ensure_cuda_torch()           # force a CUDA torch if needed (before torch is imported here)
    ensure_ultralytics()
    import torch                       # first torch import in this process -> picks up the GPU build
    use_gpu = want_gpu and torch.cuda.is_available()
    log("[yolo] device: %s%s" % ("GPU" if use_gpu else "CPU",
        "" if use_gpu else " (no CUDA torch; run again or pass nothing to retry GPU install)"))

    from ultralytics import YOLO
    run_dir = os.path.join(RUNS, "entity")
    last = os.path.join(run_dir, "weights", "last.pt")
    if a.fresh and os.path.isdir(run_dir):
        archive_stale_run(run_dir, "fresh run requested")
        last = os.path.join(run_dir, "weights", "last.pt")
    data_mtime = max(
        newest_mtime(DATA),
        newest_mtime(os.path.join(HERE, "yolo_real", "images", "train")),
        newest_mtime(os.path.join(HERE, "yolo_real", "labels", "train")),
        newest_mtime(os.path.join(HERE, "yolo_real", "images", "val")),
        newest_mtime(os.path.join(HERE, "yolo_real", "labels", "val")),
    )
    if os.path.exists(last):
        weight_mtime = os.path.getmtime(last)
        done_epochs = completed_epochs(run_dir)
        old_epochs = planned_epochs(run_dir)
        if weight_mtime + 60 < data_mtime:
            archive_stale_run(run_dir, "dataset is newer than weights")
            last = os.path.join(run_dir, "weights", "last.pt")
        elif old_epochs > 0 and done_epochs >= old_epochs and old_epochs < a.epochs:
            archive_stale_run(run_dir, "old run completed %d epochs but %d requested" % (old_epochs, a.epochs))
            last = os.path.join(run_dir, "weights", "last.pt")

    if os.path.exists(last):
        log("[yolo] resuming from %s" % last)
        model = YOLO(last)
        model.train(resume=True)
    else:
        pre = os.path.join(HERE, a.model)   # pre-fetched weights (avoids the broken auto-download)
        model = YOLO(pre if os.path.exists(pre) else a.model)
        cache = False if a.cache == "false" else a.cache
        model.train(data=DATA, epochs=a.epochs, imgsz=a.imgsz, batch=a.batch, workers=a.workers,
                     device=(0 if use_gpu else "cpu"), project=RUNS, name="entity",
                     exist_ok=True, patience=a.patience, verbose=True, cache=cache,
                     cos_lr=True, close_mosaic=10, plots=True, single_cls=False)

    best = os.path.join(RUNS, "entity", "weights", "best.pt")
    if not os.path.exists(best): best = last
    if not os.path.exists(best):
        raise RuntimeError("[yolo] no trained weights found -> abort export")
    validate_training_metrics(os.path.join(RUNS, "entity"), a.min_map50, a.min_map5095)
    log("[yolo] exporting ONNX from %s" % best)
    m = YOLO(best)
    onnx = m.export(format="onnx", opset=12, imgsz=a.imgsz, simplify=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    dst = os.path.join(OUT_DIR, "entity.onnx")
    backup_existing_export(dst)
    shutil.copy(str(onnx), dst)
    import json as _json, yaml as _yaml
    try:
        _names = _yaml.safe_load(open(DATA, encoding="utf-8")).get("names") or ["entity"]
    except Exception:
        _names = ["entity"]
    open(os.path.join(OUT_DIR, "entity_meta.json"), "w").write(
        _json.dumps({
            "imgsz": a.imgsz,
            "classes": _names,
            "format": "yolo11",
            "profile": "ro-fast-entity",
            "recommended": {
                "monster_min_score": 0.35,
                "monster_runtime_floor": 0.15,
                "other_min_score": 0.55,
                "nms_iou": 0.45,
                "tracker": "ByteTrackLite"
            }
        }, indent=2))
    log("[yolo] DONE -> %s (%d KB)" % (os.path.relpath(dst, HERE), os.path.getsize(dst)//1024))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc(); sys.exit(1)
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
