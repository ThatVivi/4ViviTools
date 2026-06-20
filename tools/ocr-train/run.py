import argparse, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))

def log(m): print(m, flush=True)

def has_cuda():
    try:
        return subprocess.call(["nvidia-smi"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) == 0
    except Exception:
        return False

def ensure_env(use_gpu):
    if use_gpu:
        # Paddle 3.x GPU (CUDA 12.6 + cuDNN 9). Uses GPU for compute + CPU for data loading.
        subprocess.call([sys.executable, "-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu"])
        subprocess.check_call([sys.executable, "-m", "pip", "install", "paddlepaddle-gpu==3.2.1",
                               "-i", "https://www.paddlepaddle.org.cn/packages/stable/cu126/"])
    else:
        # Paddle 3.x CPU.
        subprocess.call([sys.executable, "-m", "pip", "uninstall", "-y", "paddlepaddle-gpu"])
        subprocess.check_call([sys.executable, "-m", "pip", "install", "paddlepaddle==3.2.1",
                               "-i", "https://www.paddlepaddle.org.cn/packages/stable/cpu/"])
    subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", os.path.join(HERE, "requirements.txt")])
    # Paddle 3 needs a modern protobuf.
    subprocess.check_call([sys.executable, "-m", "pip", "install", "protobuf>=4.25.8"])

def get_paddleocr_repo():
    repo = os.path.join(HERE, "PaddleOCR")
    train_py = os.path.join(repo, "tools", "train.py")
    if os.path.exists(repo) and not os.path.exists(train_py):
        shutil.rmtree(repo, ignore_errors=True)   # stale / partial clone
    if not os.path.exists(repo):
        subprocess.check_call(["git", "clone", "--depth", "1",
                               "https://github.com/PaddlePaddle/PaddleOCR.git", repo])
    return repo

def current_onnx_reader():
    # Offline real-crop reader. Stub: returns "" so unlabeled real crops become '###'
    # (ignored in training). Synthetic data still trains the model.
    def read(img):
        return ""
    return read

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--user-images", default=os.path.join(HERE, "user_images"))
    ap.add_argument("--reference", default=os.path.join(HERE, "reference", "template.json"))
    ap.add_argument("--count", type=int, default=5000)
    ap.add_argument("--epochs", type=int, default=100)
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--cpu", action="store_true", help="(default) CPU training")
    ap.add_argument("--gpu", action="store_true", help="Use GPU (Paddle 3.2 + CUDA 12.6 + cuDNN 9)")
    a = ap.parse_args()

    use_gpu = a.gpu and has_cuda() and not a.cpu
    log(f"[1/6] device: {'GPU+CPU' if use_gpu else 'CPU only'}")
    ensure_env(use_gpu)

    work = os.path.join(HERE, "work")
    shutil.rmtree(work, ignore_errors=True)   # fresh each run (no manual cleanup needed)
    os.makedirs(work, exist_ok=True)
    sys.path.insert(0, HERE)
    import synth, autolabel, build_dataset, train_export

    log("[2/6] synthetic generation")
    synth.generate(os.path.join(work, "synth"), count=a.count, fonts_dir=os.path.join(HERE, "fonts"))

    log("[3/6] auto-label real screenshots")
    real_out = os.path.join(work, "real")
    has_imgs = os.path.isdir(a.user_images) and any(os.scandir(a.user_images))
    if os.path.exists(a.reference) and has_imgs:
        autolabel.label_folder(a.user_images, a.reference, real_out, current_onnx_reader())
    else:
        os.makedirs(os.path.join(real_out, "crops"), exist_ok=True)
        open(os.path.join(real_out, "rec_gt.txt"), "w").write("")
        log("    (no reference/template.json or empty user_images -> synthetic only)")

    log("[4/6] merge + split")
    data = os.path.join(work, "dataset")
    build_dataset.merge_and_split(os.path.join(work, "synth"), real_out, data)

    log("[5/6] train + export")
    repo = get_paddleocr_repo()
    cfg = train_export.find_rec_config(repo)
    log(f"    config: {os.path.relpath(cfg, repo)}")
    pre = train_export.ensure_pretrained(work)
    save_dir = os.path.join(work, "output", "rec_ro")
    dict_path = os.path.join(HERE, "ppocrv5_latin_dict.txt")
    onnx = train_export.run(repo, cfg, work, save_dir, pre, data, dict_path, a.epochs, use_gpu)

    log("[6/6] install model")
    dst = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "v5",
                       "latin_PP-OCRv5_rec_mobile_infer.onnx")
    backup = dst + ".bak"
    if os.path.exists(dst) and not os.path.exists(backup):
        shutil.copy(dst, backup)
    shutil.copy(onnx, dst)
    log("DONE")

if __name__ == "__main__":
    main()
