#!/usr/bin/env python3
"""
run.py -- ONE command does the whole OCR training sequence for 4ViviTools.

  python tools/ocr-train/run.py

Sequence (all in this file):
  [1] pick device         -> auto-GPU on the RTX 2060 SUPER (CUDA present), else CPU
  [2] build corpus        -> regenerate corpus/*.txt from gamedata.json (skills/monsters/items/...)
  [3] render training set -> user_images/crops/*.png + train_list/val_list (full game vocab + HUD/numbers)
  [4] (optional) real     -> auto-label any screenshots in --real-images against reference/template.json
  [5] install paddle      -> paddlepaddle(-gpu) 3.2.1 + PaddleOCR repo + deps
  [6] train + export      -> fine-tune rec model, export to ONNX, install into the app's models/v5

The shipped artifact is ONE file: src/RapidOcrNet/models/v5/latin_PP-OCRv5_rec_mobile_infer.onnx
Players never train -- they just run the tool with that model baked in.

Flags:
  --cpu / --gpu     force device (default: auto)
  --epochs N        training epochs (default 20)
  --total N         total rendered samples (default 0 = built-in per-category weights, ~24k)
  --skip-data       reuse the existing user_images set (don't re-render)
  --skip-corpus     don't rebuild corpus from gamedata
  --real-images DIR folder of real screenshots to auto-label and mix in
"""
import argparse, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAMEDATA = os.path.join(HERE, "..", "..", "src", "4rVivi.Core", "Data", "gamedata.json")

def log(m): print(m, flush=True)

def has_cuda():
    try:
        return subprocess.call(["nvidia-smi"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) == 0
    except Exception:
        return False

def py(*args):
    subprocess.check_call([sys.executable, *args])

def ensure_env(use_gpu):
    if use_gpu:
        subprocess.call([sys.executable, "-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu"])
        subprocess.check_call([sys.executable, "-m", "pip", "install", "paddlepaddle-gpu==3.2.1",
                               "-i", "https://www.paddlepaddle.org.cn/packages/stable/cu126/"])
        # Paddle 3.2.1 is built against cuDNN 9.9 but pins 9.5; force the matching cuDNN to
        # avoid the "compiled with CUDNN 9.9 but 9.5 in your machine" incompatibility.
        subprocess.check_call([sys.executable, "-m", "pip", "install", "--user", "--upgrade",
                               "--no-deps", "nvidia-cudnn-cu12==9.9.0.52"])
    else:
        subprocess.call([sys.executable, "-m", "pip", "uninstall", "-y", "paddlepaddle-gpu"])
        subprocess.check_call([sys.executable, "-m", "pip", "install", "paddlepaddle==3.2.1",
                               "-i", "https://www.paddlepaddle.org.cn/packages/stable/cpu/"])
    subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", os.path.join(HERE, "requirements.txt")])
    subprocess.check_call([sys.executable, "-m", "pip", "install", "protobuf>=4.25.8"])

def get_paddleocr_repo():
    repo = os.path.join(HERE, "PaddleOCR")
    train_py = os.path.join(repo, "tools", "train.py")
    if os.path.exists(repo) and not os.path.exists(train_py):
        shutil.rmtree(repo, ignore_errors=True)
    if not os.path.exists(repo):
        subprocess.check_call(["git", "clone", "--depth", "1",
                               "https://github.com/PaddlePaddle/PaddleOCR.git", repo])
    return repo

def stub_reader():
    def read(img): return ""
    return read

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--epochs", type=int, default=20)
    ap.add_argument("--total", type=int, default=0)
    ap.add_argument("--cpu", action="store_true")
    ap.add_argument("--gpu", action="store_true")
    ap.add_argument("--skip-data", action="store_true")
    ap.add_argument("--skip-corpus", action="store_true")
    ap.add_argument("--real-images", default="")
    ap.add_argument("--user-images", default=os.path.join(HERE, "user_images"))
    ap.add_argument("--reference", default=os.path.join(HERE, "reference", "template.json"))
    a = ap.parse_args()

    use_gpu = (a.gpu or has_cuda()) and not a.cpu
    log("[1/6] device: %s" % ("GPU+CPU" if use_gpu else "CPU only"))

    if not a.skip_corpus and os.path.exists(GAMEDATA):
        log("[2/6] build corpus from gamedata.json")
        py(os.path.join(HERE, "build_corpus.py"), "--gamedata", GAMEDATA)
    else:
        log("[2/6] corpus: skipped")

    if a.skip_data:
        log("[3/6] render: skipped (--skip-data, reusing user_images)")
    else:
        log("[3/6] render training set (uses the RO Gulim font) -> %s" % os.path.relpath(a.user_images, HERE))
        cmd = [os.path.join(HERE, "build_training_set.py"), "--out", a.user_images]
        if a.total > 0: cmd += ["--total", str(a.total)]
        py(*cmd)
        py(os.path.join(HERE, "build_training_set.py"), "--out", a.user_images, "--finalize",
           *(["--total", str(a.total)] if a.total > 0 else []))

    sys.path.insert(0, HERE)
    import autolabel, build_dataset, train_export

    real_dir = os.path.join(HERE, "real")
    if os.path.exists(os.path.join(real_dir, "rec_gt.txt")):
        log("[4/6] merge labeled real crops from real/ (make them via label_screenshots.py)")
        work = os.path.join(HERE, "work"); os.makedirs(work, exist_ok=True)
        data = os.path.join(work, "dataset")
        # real_ratio huge so the FULL synthetic set is kept (no down-capping to the real count)
        build_dataset.merge_and_split(a.user_images, real_dir, data, real_ratio=100000)
    else:
        log("[4/6] real crops: none -> training on rendered set only")
        data = a.user_images

    log("[5/6] install paddle + PaddleOCR")
    stamp = os.path.join(HERE, ".env_ready")
    if os.path.exists(stamp):
        log("    deps already installed -> skipping (delete tools/ocr-train/.env_ready to reinstall)")
    else:
        ensure_env(use_gpu)
        open(stamp, "w").write("ok")
    repo = get_paddleocr_repo()

    log("[6/6] train + export + install")
    cfg = train_export.find_rec_config(repo)
    log("    config: %s" % os.path.relpath(cfg, repo))
    work = os.path.join(HERE, "work"); os.makedirs(work, exist_ok=True)
    pre = train_export.ensure_pretrained(work)
    save_dir = os.path.join(work, "output", "rec_ro")
    dict_path = os.path.join(HERE, "ppocrv5_latin_dict.txt")
    onnx = train_export.run(repo, cfg, work, save_dir, pre, data, dict_path, a.epochs, use_gpu)

    dst = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "v5",
                       "latin_PP-OCRv5_rec_mobile_infer.onnx")
    if onnx and os.path.exists(onnx):
        if os.path.exists(dst) and not os.path.exists(dst + ".bak"):
            shutil.copy(dst, dst + ".bak")
        shutil.copy(onnx, dst)
        log("DONE -> model installed at src/RapidOcrNet/models/v5/")
    else:
        log("in-env ONNX not produced -> the clean-venv convert step will export + install it.")

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
        sys.exit(1)   # let the runner stop instead of silently shipping the old model
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
