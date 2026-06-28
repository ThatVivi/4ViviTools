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

# --- PATCH: validate OCR dataset before training ---
def validate_dataset_lists(dataset_root):
    from PIL import Image
    import os

    for list_name in ("train_list.txt", "val_list.txt"):
        list_path = os.path.join(dataset_root, list_name)
        if not os.path.exists(list_path):
            continue

        cleaned = []
        removed = 0

        with open(list_path, "r", encoding="utf-8") as f:
            lines = f.readlines()

        expected_h = None

        for line in lines:
            try:
                img_path = line.split("\t")[0].strip()
                if not os.path.exists(img_path):
                    removed += 1
                    continue

                with Image.open(img_path) as img:
                    w, h = img.size

                    if w <= 1 or h <= 1:
                        removed += 1
                        continue

                    if expected_h is None:
                        expected_h = h

                    if h != expected_h:
                        removed += 1
                        continue

                cleaned.append(line)

            except Exception:
                removed += 1

        with open(list_path, "w", encoding="utf-8") as f:
            f.writelines(cleaned)

        print(f"[PATCH] {list_name}: removed {removed} bad entries")

# --- END PATCH ---


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

    if not a.skip_data:
        log("[3/6] render training set -> %s" % os.path.relpath(a.user_images, HERE))
        cmd = [os.path.join(HERE, "build_training_set.py"), "--out", a.user_images]
        if a.total > 0: cmd += ["--total", str(a.total)]
        py(*cmd)
        py(os.path.join(HERE, "build_training_set.py"), "--out", a.user_images, "--finalize",
           *(["--total", str(a.total)] if a.total > 0 else []))
    else:
        log("[3/6] render: skipped (reusing user_images)")

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

    validate_dataset_lists(a.user_images)

    log("[5/6] install paddle + PaddleOCR")
    ensure_env(use_gpu)
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
    if os.path.exists(dst) and not os.path.exists(dst + ".bak"):
        shutil.copy(dst, dst + ".bak")
    shutil.copy(onnx, dst)
    log("DONE -> model installed at src/RapidOcrNet/models/v5/")

if __name__ == "__main__":
    main()
