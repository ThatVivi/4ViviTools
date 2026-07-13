#!/usr/bin/env python3
"""
One-file training runner for Vivi's machine:
  CPU: i7-11700K
  GPU: RTX 2060 Super 8GB
  RAM: 32GB

It prepares monster frames from the GRF, trains/exports OCR text, trains/exports the icon/sprite
embedder, mixes synthetic GRF monster scenes into the paid YOLO dataset, trains/exports YOLO, then
builds the app. Every child script is resumable; re-run this file if Windows sleeps or a stage fails.
"""
import argparse
import glob
import json
import os
import shutil
import subprocess
import sys
import pickle
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
MONSTER_FOLDER = "\uBAAC\uC2A4\uD130"
LOG_PATH = os.path.join(HERE, "last_full_training.log")
STATE_PATH = os.path.join(HERE, ".full_training_state.json")


class Tee:
    def __init__(self, *streams):
        self.streams = streams

    def write(self, data):
        for stream in self.streams:
            stream.write(data)
            stream.flush()

    def flush(self):
        for stream in self.streams:
            stream.flush()


def run(label, args, cwd=HERE):
    print("\n" + "=" * 78, flush=True)
    print("[4Vivi full training] " + label, flush=True)
    print("=" * 78, flush=True)
    print(" ".join('"%s"' % a if " " in a else a for a in args), flush=True)
    env = dict(os.environ)
    env["NO_PAUSE"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    env.setdefault("OCR_CPU_THREADS", "8")
    proc = subprocess.Popen(
        args,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )
    assert proc.stdout is not None
    for line in proc.stdout:
        print(line, end="", flush=True)
    rc = proc.wait()
    if rc:
        raise subprocess.CalledProcessError(rc, args)


def py(script, *args):
    return [sys.executable, os.path.join(HERE, script), *map(str, args)]


def count_spr(path):
    try:
        return sum(1 for _ in glob.iglob(os.path.join(path, "**", "*.spr"), recursive=True))
    except Exception:
        return 0


def claude_monster_dirs():
    root = os.path.join(os.path.dirname(REPO), "claude", "data", "sprite")
    if not os.path.isdir(root):
        return []
    dirs = []
    for name in os.listdir(root):
        p = os.path.join(root, name)
        if os.path.isdir(p) and count_spr(p) > 0:
            dirs.append(p)
    return sorted(dirs, key=count_spr, reverse=True)


def monster_src():
    candidates = [
        os.path.join(os.path.dirname(REPO), "claude", "data", "sprite", "¸ó½ºÅÍ"),
        os.path.join(os.path.dirname(REPO), "claude", "data", "sprite", MONSTER_FOLDER),
        os.path.join(REPO, "GRF", "data", "sprite", MONSTER_FOLDER),
        os.path.join(REPO, "GRF", "kRO Data", "data", "sprite", MONSTER_FOLDER),
        os.path.join(HERE, "OCR Names", "data", "sprite", MONSTER_FOLDER),
    ]
    candidates = claude_monster_dirs() + candidates
    for p in candidates:
        if os.path.isdir(p):
            return p
    return candidates[0]


def ensure_export_venv():
    vpy = os.path.join(HERE, "ocr_export", "Scripts", "python.exe")
    if not os.path.exists(vpy):
        run("Create clean ONNX export venv", [sys.executable, "-m", "venv", os.path.join(HERE, "ocr_export")])
    run("Install ONNX export packages", [
        vpy, "-m", "pip", "install", "--upgrade", "pip",
    ])
    run("Install Paddle/ONNX conversion stack", [
        vpy, "-m", "pip", "install", "--no-cache-dir",
        "packaging", "setuptools", "wheel", "protobuf==4.25.8", "onnx==1.17.0",
        "paddle2onnx==2.0.2rc3", "paddlepaddle==3.1.0",
    ])
    return vpy


def load_state():
    try:
        return json.load(open(STATE_PATH, encoding="utf-8"))
    except Exception:
        return {"completed": {}}


def save_state(state):
    tmp = STATE_PATH + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2)
    os.replace(tmp, STATE_PATH)


def reset_selected_stages(state, stage_ids):
    completed = state.setdefault("completed", {})
    removed = []
    for stage_id in stage_ids:
        if stage_id in completed:
            completed.pop(stage_id, None)
            removed.append(stage_id)
    if removed:
        save_state(state)
        print("[resume] reset checkpoints: " + ", ".join(removed), flush=True)
    else:
        print("[resume] no matching checkpoints needed reset", flush=True)


def stage_signature(args):
    return " ".join(str(a) for a in args)


def run_stage(state, stage_id, label, args, cwd=HERE, force=False, verify=None, accept_existing=False):
    completed = state.setdefault("completed", {})
    sig = stage_signature(args)
    if not force and completed.get(stage_id, {}).get("signature") == sig:
        if verify is not None:
            try:
                verify()
                print("\n" + "=" * 78, flush=True)
                print("[4Vivi full training] SKIP verified completed: " + label, flush=True)
                print("=" * 78, flush=True)
                return
            except Exception as ex:
                print("\n" + "=" * 78, flush=True)
                print("[4Vivi full training] completed marker is stale: " + label, flush=True)
                print("[verify] " + str(ex), flush=True)
                print("=" * 78, flush=True)
        else:
            print("\n" + "=" * 78, flush=True)
            print("[4Vivi full training] SKIP completed: " + label, flush=True)
            print("=" * 78, flush=True)
            return
    if not force and accept_existing and verify is not None:
        try:
            verify()
            completed[stage_id] = {
                "label": label,
                "signature": sig,
                "completed_at": datetime.now().isoformat(timespec="seconds"),
            }
            save_state(state)
            print("\n" + "=" * 78, flush=True)
            print("[4Vivi full training] SKIP verified existing output: " + label, flush=True)
            print("=" * 78, flush=True)
            return
        except Exception:
            pass
    run(label, args, cwd=cwd)
    if verify is not None:
        verify()
    completed[stage_id] = {
        "label": label,
        "signature": sig,
        "completed_at": datetime.now().isoformat(timespec="seconds"),
    }
    save_state(state)


def require_file(path, label):
    if not os.path.isfile(path):
        raise FileNotFoundError(f"{label} missing: {path}")


def require_dir(path, label):
    if not os.path.isdir(path):
        raise FileNotFoundError(f"{label} missing: {path}")


def run_check(label, args, cwd=REPO):
    print(f"[preflight] {label}: {' '.join(args)}", flush=True)
    subprocess.check_call(args, cwd=cwd)


def require_python_imports():
    checks = (
        ("supervision", "Supervision/ByteTrack dataset QC"),
        ("lap", "ByteTrack assignment backend from lapx"),
        ("ultralytics", "YOLO train/export"),
        ("onnxslim", "YOLO ONNX export slimming"),
    )
    for module, label in checks:
        try:
            __import__(module)
        except Exception as ex:
            raise RuntimeError(
                f"Python dependency missing for {label}: import {module} failed ({ex}). "
                "Run RUN_EVERYTHING_2060S.bat so it installs tools/ocr-train/requirements.txt."
            ) from ex
        print(f"[preflight] dependency OK: {module} ({label})", flush=True)


def require_min_glob(pattern, min_count, label):
    n = len(glob.glob(pattern, recursive=True))
    if n < min_count:
        raise RuntimeError(f"{label} expected at least {min_count}, found {n}: {pattern}")
    print(f"[verify] {label}: {n}", flush=True)


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


def verify_monster_frames():
    require_min_glob(os.path.join(HERE, "icons", "raw", "mob__*", "*.png"), 1000, "monster sprite frames")


def verify_yolo_real():
    require_file(os.path.join(HERE, "yolo_real", "data.yaml"), "merged YOLO data.yaml")
    require_min_glob(os.path.join(HERE, "yolo_real", "images", "train", "*.*"), 100, "merged YOLO train images")
    require_min_glob(os.path.join(HERE, "yolo_real", "labels", "train", "*.txt"), 100, "merged YOLO train labels")


def verify_yolo_synth():
    require_file(os.path.join(HERE, "yolo", "data.yaml"), "synthetic YOLO data.yaml")
    require_min_glob(os.path.join(HERE, "yolo", "images", "train", "*.jpg"), 100, "synthetic YOLO train images")


def verify_yolo_qc():
    qc = os.path.join(HERE, "yolo_real", "qc_supervision")
    require_file(os.path.join(qc, "report.json"), "YOLO QC report")
    require_file(os.path.join(qc, "train_sample.jpg"), "YOLO train QC sample sheet")
    require_file(os.path.join(qc, "val_sample.jpg"), "YOLO val QC sample sheet")
    with open(os.path.join(qc, "report.json"), encoding="utf-8") as f:
        report = json.load(f)
    splits = report.get("splits", {})
    train = splits.get("train", {})
    val = splits.get("val", {})
    if int(train.get("missing_labels", 0)) > 0 or int(val.get("missing_labels", 0)) > 0:
        raise RuntimeError("YOLO QC found missing label files.")
    if int(train.get("boxes", 0)) < 100 or int(val.get("boxes", 0)) < 10:
        raise RuntimeError("YOLO QC found too few boxes; dataset may be broken.")
    print("[verify] YOLO QC: train boxes=%s val boxes=%s" % (train.get("boxes", "?"), val.get("boxes", "?")), flush=True)


def verify_text_model():
    p = os.path.join(REPO, "src", "RapidOcrNet", "models", "v5", "latin_PP-OCRv5_rec_mobile_infer.onnx")
    require_file(p, "text OCR ONNX")
    if os.path.getsize(p) < 1024 * 1024:
        raise RuntimeError("text OCR ONNX is suspiciously small: %s" % p)
    ck = os.path.join(HERE, "work", "output", "rec_ro", "latest.pdparams")
    if os.path.isfile(ck) and os.path.getmtime(p) + 60 < os.path.getmtime(ck):
        raise RuntimeError("text OCR ONNX is older than the latest trained checkpoint; conversion must run.")
    print("[verify] text OCR ONNX: %d KB" % (os.path.getsize(p) // 1024), flush=True)


def verify_text_checkpoint():
    base = os.path.join(HERE, "work", "output", "rec_ro", "latest")
    for ext in (".pdparams", ".pdopt", ".states"):
        require_file(base + ext, "text OCR checkpoint " + ext)
    with open(base + ".states", "rb") as f:
        state = pickle.load(f)
    epoch = int(state.get("epoch", 0))
    if epoch < 6:
        raise RuntimeError("text OCR checkpoint is not complete enough: epoch=%d" % epoch)
    print("[verify] text OCR checkpoint epoch=%d" % epoch, flush=True)


def verify_icon_model():
    icon_dir = os.path.join(REPO, "src", "RapidOcrNet", "models", "icons")
    for name in ("icon_embedder.onnx", "icon_refs.bin", "labels.txt", "icon_meta.json"):
        require_file(os.path.join(icon_dir, name), "icon model " + name)
    labels = open(os.path.join(icon_dir, "labels.txt"), encoding="utf-8", errors="replace").read().splitlines()
    bad = [ln for ln in labels if "\titems__" in ln or "\titemsbyname__" in ln or "\titem__" in ln
           or "\tmap__" in ln or "\tspr_" in ln]
    if bad:
        raise RuntimeError("icon model contains non-target item/map/spr labels; first bad label: " + bad[0])
    skill_refs = sum(1 for ln in labels if "\tskills__" in ln or "\tskill__" in ln)
    mob_refs = sum(1 for ln in labels if "\tmob__" in ln)
    if skill_refs <= 0 or mob_refs <= 0:
        raise RuntimeError("icon model must contain both skill and monster labels.")
    print("[verify] icon labels: skills=%d monster_refs=%d total=%d" % (skill_refs, mob_refs, len(labels)), flush=True)
    require_min_glob(os.path.join(HERE, "icons", "raw", "mob__*", "*.png"), 1000, "monster sprite frames in icon bank source")


def verify_yolo_model():
    p = os.path.join(REPO, "src", "RapidOcrNet", "models", "yolo", "entity.onnx")
    require_file(p, "YOLO entity ONNX")
    if os.path.getsize(p) < 1024 * 1024:
        raise RuntimeError("YOLO entity ONNX is suspiciously small: %s" % p)
    meta = os.path.join(REPO, "src", "RapidOcrNet", "models", "yolo", "entity_meta.json")
    require_file(meta, "YOLO metadata")
    best = os.path.join(HERE, "yolo_real", "runs", "entity", "weights", "best.pt")
    last = os.path.join(HERE, "yolo_real", "runs", "entity", "weights", "last.pt")
    trained = best if os.path.isfile(best) else last
    require_file(trained, "YOLO trained weights")
    data_mtime = max(
        newest_mtime(os.path.join(HERE, "yolo_real", "data.yaml")),
        newest_mtime(os.path.join(HERE, "yolo_real", "images", "train")),
        newest_mtime(os.path.join(HERE, "yolo_real", "labels", "train")),
        newest_mtime(os.path.join(HERE, "yolo_real", "images", "val")),
        newest_mtime(os.path.join(HERE, "yolo_real", "labels", "val")),
    )
    if os.path.getmtime(trained) + 60 < data_mtime:
        raise RuntimeError("YOLO weights are older than yolo_real; retrain required.")
    print("[verify] YOLO entity ONNX: %d KB" % (os.path.getsize(p) // 1024), flush=True)


def verify_release_build():
    require_file(os.path.join(REPO, "src", "4rVivi.App", "bin", "Release", "net8.0-windows10.0.19041.0", "4rVivi.dll"), "release app dll")


def preflight(a):
    print("[preflight] 4ViviTools full training checks", flush=True)
    print("[preflight] repo: " + REPO, flush=True)
    print("[preflight] python: " + sys.executable, flush=True)
    if sys.version_info < (3, 10):
        raise RuntimeError("Python 3.10+ is required.")
    if shutil.which("dotnet") is None:
        raise RuntimeError("dotnet was not found in PATH.")
    if shutil.which("git") is None:
        raise RuntimeError("git was not found in PATH.")
    require_python_imports()
    if shutil.which("nvidia-smi") is None and not a.cpu:
        print("[preflight] WARNING: nvidia-smi not found; use --cpu if GPU setup is not ready.", flush=True)
    elif not a.cpu:
        subprocess.check_call(["nvidia-smi"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("[preflight] NVIDIA driver visible.", flush=True)

    total, used, free = shutil.disk_usage(REPO)
    free_gb = free / (1024 ** 3)
    print("[preflight] free disk: %.1f GB" % free_gb, flush=True)
    if free_gb < 40:
        raise RuntimeError("Free disk is below 40 GB. Full training can run out of space.")
    if free_gb < 80:
        print("[preflight] WARNING: under 80 GB free; full training may be tight.", flush=True)

    for script in (
        "build_monster_names.py", "extract_maps.py", "run.py", "build_icon_model.py",
        "convert_in_venv.py", "merge_datasets.py", "gen_yolo_scenes.py",
        "mix_synthetic_yolo.py", "yolo_qc_supervision.py", "train_yolo.py", "train_everything_2060s.py",
    ):
        require_file(os.path.join(HERE, script), script)
    require_file(os.path.join(REPO, "4rVivi.sln"), "solution")
    require_file(os.path.join(REPO, "src", "4rVivi.App", "Assets", "iconpack.zip"), "iconpack")
    require_file(os.path.join(REPO, "src", "4rVivi.Core", "Data", "gamedata.json"), "gamedata")

    src = monster_src()
    sprs = count_spr(src)
    print("[preflight] monster source: %s" % src, flush=True)
    print("[preflight] monster .spr files: %d" % sprs, flush=True)
    if sprs == 0:
        raise RuntimeError("No monster .spr files found.")

    if not a.skip_yolo:
        require_dir(os.path.join(HERE, "TrainingData"), "paid YOLO TrainingData")
        yaml_count = len(glob.glob(os.path.join(HERE, "TrainingData", "**", "data.y*ml"), recursive=True))
        print("[preflight] Roboflow data.yaml files: %d" % yaml_count, flush=True)
        if yaml_count == 0:
            raise RuntimeError("No Roboflow data.yaml files found under TrainingData.")

    if not a.skip_text:
        font_count = len(glob.glob(os.path.join(HERE, "fonts", "*.ttf"))) + len(glob.glob(os.path.join(HERE, "fonts", "*.ttc")))
        print("[preflight] OCR fonts: %d" % font_count, flush=True)
        if font_count == 0:
            raise RuntimeError("No OCR fonts found under tools/ocr-train/fonts.")

    raw_mob_frames = len(glob.glob(os.path.join(HERE, "icons", "raw", "mob__*", "*.png")))
    print("[preflight] existing mob raw frames: %d" % raw_mob_frames, flush=True)

    run_check("Python syntax", [sys.executable, "-m", "py_compile",
        os.path.join(HERE, "build_monster_names.py"),
        os.path.join(HERE, "build_icon_model.py"),
        os.path.join(HERE, "run.py"),
        os.path.join(HERE, "convert_in_venv.py"),
        os.path.join(HERE, "merge_datasets.py"),
        os.path.join(HERE, "gen_yolo_scenes.py"),
        os.path.join(HERE, "mix_synthetic_yolo.py"),
        os.path.join(HERE, "yolo_qc_supervision.py"),
        os.path.join(HERE, "ingest_video.py"),
        os.path.join(HERE, "train_yolo.py"),
        os.path.join(HERE, "train_everything_2060s.py")])
    run_check("Release build smoke", ["dotnet", "build", os.path.join(REPO, "src", "4rVivi.App", "4rVivi.App.csproj"), "-c", "Release", "--nologo"])
    print("[preflight] OK - expensive training stages can start.", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--text-epochs", type=int, default=6)
    ap.add_argument("--icon-epochs", type=int, default=24)
    ap.add_argument("--icon-variants", type=int, default=24)
    ap.add_argument("--monster-refs", type=int, default=0, help="distinct monster frames per sprite; 0 = all distinct frames")
    ap.add_argument("--yolo-scenes", type=int, default=6000)
    ap.add_argument("--yolo-epochs", type=int, default=60)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--force-monsters", action="store_true", help="rewrite all mob__ monster PNG frames from the GRF")
    ap.add_argument("--skip-text", action="store_true")
    ap.add_argument("--skip-icons", action="store_true")
    ap.add_argument("--skip-yolo", action="store_true")
    ap.add_argument("--skip-yolo-qc", action="store_true")
    ap.add_argument("--cpu", action="store_true")
    ap.add_argument("--preflight-only", action="store_true", help="run all cheap safety checks, then exit before training")
    ap.add_argument("--reset-checkpoints", action="store_true", help="forget completed stage checkpoints and run every enabled stage again")
    ap.add_argument("--reset-yolo-checkpoints", action="store_true", help="forget only YOLO dataset/QC/train/build checkpoints")
    ap.add_argument("--fresh-yolo-train", action="store_true", help="archive the previous YOLO run before training")
    ap.add_argument("--min-yolo-map50", type=float, default=0.70)
    ap.add_argument("--min-yolo-map5095", type=float, default=0.35)
    a = ap.parse_args()

    log_file = open(LOG_PATH, "a", encoding="utf-8", buffering=1)
    sys.stdout = Tee(sys.stdout, log_file)
    sys.stderr = Tee(sys.stderr, log_file)
    print("\n\n===== 4Vivi full training run %s =====" % datetime.now().isoformat(timespec="seconds"), flush=True)
    preflight(a)
    if a.preflight_only:
        print("[preflight-only] Checks passed. No expensive training was started.", flush=True)
        return

    if a.reset_checkpoints and os.path.exists(STATE_PATH):
        os.remove(STATE_PATH)
    state = load_state()
    if a.reset_yolo_checkpoints:
        reset_selected_stages(state, (
            "09_yolo_merge",
            "10_yolo_synth",
            "11_yolo_mix",
            "11b_yolo_qc_supervision",
            "12_yolo_train",
            "13_build",
        ))

    src = monster_src()
    print("[hardware] i7-11700K / RTX 2060 Super 8GB / 32GB RAM profile", flush=True)
    print("[monster-src] " + src, flush=True)
    print("[resume] checkpoint file: " + STATE_PATH, flush=True)
    print("[log] full log: " + LOG_PATH, flush=True)

    monster_args = ["--src", src, "--refs", a.monster_refs, "--no-append"]
    if a.force_monsters:
        monster_args.append("--force")
    run_stage(state, "01_monsters", "Extract ALL GRF monsters as multi-frame mob__ references",
              py("build_monster_names.py", *monster_args), verify=verify_monster_frames)

    run_stage(state, "02_maps", "Extract map minimaps for backgrounds/map recognition", py("extract_maps.py"))

    if not a.skip_text:
        text_args = ["--gpu", "--epochs", a.text_epochs]
        if a.cpu:
            text_args = ["--cpu", "--epochs", a.text_epochs]
        run_stage(state, "03_text_train", "Train RO text OCR recognizer", py("run.py", *text_args),
                  verify=verify_text_checkpoint, accept_existing=True)
        vpy = ensure_export_venv()
        run_stage(state, "04_text_convert", "Convert text/icon models to ONNX in clean venv",
                  [vpy, os.path.join(HERE, "convert_in_venv.py")], verify=verify_text_model, accept_existing=True)
    else:
        print("[skip] text OCR", flush=True)

    if not a.skip_icons:
        icon_args = ["--epochs", a.icon_epochs, "--variants", a.icon_variants]
        if a.cpu:
            icon_args.append("--cpu")
        run_stage(state, "05_icon_train_monsters_skills", "Train monster + skill sprite embedder", py("build_icon_model.py", *icon_args))
        vpy = ensure_export_venv()
        run_stage(state, "06_icon_convert_monsters_skills", "Convert monster + skill embedder to ONNX / refresh banks",
                  [vpy, os.path.join(HERE, "convert_in_venv.py")], verify=verify_icon_model)
        append_args = ["--src", src, "--refs", a.monster_refs]
        if a.force_monsters:
            append_args.append("--force")
        run_stage(state, "07_monster_bank_monsters_skills", "Top up runtime monster reference bank",
                  py("build_monster_names.py", *append_args), verify=verify_icon_model)
    else:
        print("[skip] icon/sprite embedder", flush=True)

    if not a.skip_yolo:
        yolo_recipe = "ro_video_hardneg_v2"
        run_stage(state, "09_yolo_merge", "Merge paid YOLO datasets + hard negatives",
                  py("merge_datasets.py", "--reset", "--empty-negatives", 0.12, "--recipe", yolo_recipe), verify=verify_yolo_real)
        run_stage(state, "10_yolo_synth", "Generate GRF synthetic monster scenes", py("gen_yolo_scenes.py",
            "--resume", "--scenes", a.yolo_scenes, "--imgsz", a.imgsz, "--max-objs", 16,
            "--negative-scenes", 0.10), verify=verify_yolo_synth)
        run_stage(state, "11_yolo_mix", "Mix synthetic scenes into yolo_real", py("mix_synthetic_yolo.py",
            "--recipe", yolo_recipe), verify=verify_yolo_real)
        if not a.skip_yolo_qc:
            run_stage(state, "11b_yolo_qc_supervision", "Supervision / ByteTrack-ready YOLO dataset QC",
                      py("yolo_qc_supervision.py", "--data", os.path.join(HERE, "yolo_real", "data.yaml"),
                         "--out", os.path.join(HERE, "yolo_real", "qc_supervision"), "--max-images", 48),
                      verify=verify_yolo_qc)
        yolo_args = ["--epochs", a.yolo_epochs, "--imgsz", a.imgsz, "--batch", 16, "--workers", 8, "--cache", "ram",
                     "--min-map50", a.min_yolo_map50, "--min-map5095", a.min_yolo_map5095]
        if a.fresh_yolo_train:
            yolo_args.append("--fresh")
        if a.cpu:
            yolo_args.append("--cpu")
        run_stage(state, "12_yolo_train", "Train/export YOLO monster/entity detector", py("train_yolo.py", *yolo_args), verify=verify_yolo_model)
    else:
        print("[skip] YOLO detector", flush=True)

    run_stage(state, "13_build", "Build release app with trained models",
              ["dotnet", "build", os.path.join(REPO, "4rVivi.sln"), "-c", "Release"], cwd=REPO, verify=verify_release_build)
    print("\nDONE. Release output:", flush=True)
    print(os.path.join(REPO, "src", "4rVivi.App", "bin", "Release", "net8.0-windows10.0.19041.0"), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
