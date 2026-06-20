import os, glob, subprocess, sys, urllib.request

# Drop-in target for our runtime: latin PP-OCRv5 mobile recognition.
PRETRAIN_URL = ("https://paddle-model-ecology.bj.bcebos.com/paddlex/"
                "official_pretrained_model/latin_PP-OCRv5_mobile_rec_pretrained.pdparams")

def _fwd(p):
    return p.replace("\\", "/")

def find_rec_config(repo):
    """Prefer the latin PP-OCRv5 mobile rec config; fall back to the general v5 mobile rec."""
    pats = [
        os.path.join(repo, "configs", "rec", "**", "*latin*PP-OCRv5*mobile*rec*.yml"),
        os.path.join(repo, "configs", "rec", "**", "*latin*rec*.yml"),
        os.path.join(repo, "configs", "rec", "PP-OCRv5", "PP-OCRv5_mobile_rec.yml"),
        os.path.join(repo, "configs", "rec", "**", "*PP-OCRv5*mobile*rec*.yml"),
    ]
    for p in pats:
        hits = sorted(glob.glob(p, recursive=True))
        if hits:
            return hits[0]
    raise FileNotFoundError("No PP-OCRv5 rec config found in cloned PaddleOCR.")

def ensure_pretrained(work):
    dst = os.path.join(work, "pretrain")
    os.makedirs(dst, exist_ok=True)
    pdparams = os.path.join(dst, "latin_PP-OCRv5_mobile_rec_pretrained.pdparams")
    if not os.path.exists(pdparams):
        urllib.request.urlretrieve(PRETRAIN_URL, pdparams)
    return _fwd(pdparams[:-len(".pdparams")])  # path without extension

def _paddle2onnx(args, env):
    for cmd in ([sys.executable, "-m", "paddle2onnx"], ["paddle2onnx"]):
        try:
            subprocess.check_call(cmd + args, env=env); return
        except (FileNotFoundError, subprocess.CalledProcessError) as e:
            last = e
    raise last

def run(repo, config_yml, work, save_dir, pretrained, data_dir, dict_path, epochs, use_gpu):
    env = dict(os.environ)
    data = _fwd(data_dir); save = _fwd(save_dir); dct = _fwd(dict_path)
    train_list = data + "/train_list.txt"
    val_list = data + "/val_list.txt"

    # All overrides via -o so we never depend on the repo config's exact schema.
    overrides = [
        f"Global.pretrained_model={pretrained}",
        f"Global.save_model_dir={save}",
        f"Global.epoch_num={epochs}",
        f"Global.use_gpu={'true' if use_gpu else 'false'}",
        f"Global.character_dict_path={dct}",
        f"Train.dataset.data_dir={data}",
        f"Train.dataset.label_file_list=[{train_list}]",
        f"Train.loader.num_workers=0",
        f"Train.loader.batch_size_per_card=64",
        f"Eval.dataset.data_dir={data}",
        f"Eval.dataset.label_file_list=[{val_list}]",
        f"Eval.loader.num_workers=0",
        f"Eval.loader.batch_size_per_card=64",
    ]
    subprocess.check_call([sys.executable, os.path.join(repo, "tools", "train.py"),
                           "-c", config_yml, "-o", *overrides], env=env)

    best = os.path.join(save_dir, "best_accuracy")
    if not os.path.exists(best + ".pdparams"):
        best = os.path.join(save_dir, "latest")

    infer = os.path.join(work, "inference_rec")
    subprocess.check_call([sys.executable, os.path.join(repo, "tools", "export_model.py"),
                           "-c", config_yml, "-o",
                           f"Global.pretrained_model={_fwd(best)}",
                           f"Global.character_dict_path={dct}",
                           f"Global.save_inference_dir={_fwd(infer)}"], env=env)

    model_file = "inference.json" if os.path.exists(os.path.join(infer, "inference.json")) else "inference.pdmodel"
    onnx_out = os.path.join(work, "rec.onnx")
    _paddle2onnx(["--model_dir", infer, "--model_filename", model_file,
                  "--params_filename", "inference.pdiparams",
                  "--save_file", onnx_out, "--opset_version", "11"], env)
    return onnx_out
