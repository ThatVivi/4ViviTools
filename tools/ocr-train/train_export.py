import os, subprocess, sys, urllib.request, tarfile

PRETRAIN_URL = "https://paddleocr.bj.bcebos.com/PP-OCRv3/english/en_PP-OCRv3_rec_train.tar"

def _fwd(p):  # YAML-safe path (forward slashes; Windows accepts them)
    return p.replace("\\", "/")

def _ensure_pretrained(work):
    dst = os.path.join(work, "pretrain")
    params = os.path.join(dst, "en_PP-OCRv3_rec_train", "best_accuracy.pdparams")
    if os.path.exists(params):
        return _fwd(params[:-len(".pdparams")])
    os.makedirs(dst, exist_ok=True)
    tar = os.path.join(dst, "rec.tar")
    urllib.request.urlretrieve(PRETRAIN_URL, tar)
    with tarfile.open(tar) as t:
        t.extractall(dst)
    return _fwd(params[:-len(".pdparams")])

def write_config(template, data_dir, use_gpu, pretrained, save_dir, out_yml):
    s = open(template, encoding="utf-8").read()
    s = s.replace("__USE_GPU__", "true" if use_gpu else "false")
    s = s.replace("__DATA_DIR__", _fwd(data_dir))
    s = s.replace("__PRETRAINED__", _fwd(pretrained))
    s = s.replace("__SAVE_DIR__", _fwd(save_dir))
    open(out_yml, "w", encoding="utf-8").write(s)

def _paddle2onnx(args, env):
    # The paddle2onnx console script is often not on PATH. Prefer `python -m paddle2onnx`,
    # then fall back to the plain command.
    for cmd in ([sys.executable, "-m", "paddle2onnx"], ["paddle2onnx"]):
        try:
            subprocess.check_call(cmd + args, env=env)
            return
        except (FileNotFoundError, subprocess.CalledProcessError) as e:
            last = e
    raise last

def run(paddleocr_repo, config_yml, work, save_dir):
    env = dict(os.environ)
    # paddle's generated protobuf is old; pure-python parsing tolerates any installed protobuf.
    env["PROTOCOL_BUFFERS_PYTHON_IMPLEMENTATION"] = "python"

    subprocess.check_call([sys.executable, os.path.join(paddleocr_repo, "tools", "train.py"),
                           "-c", config_yml], env=env)

    # Prefer the best checkpoint; fall back to latest if eval never saved a best.
    best = os.path.join(save_dir, "best_accuracy")
    if not os.path.exists(best + ".pdparams"):
        best = os.path.join(save_dir, "latest")

    infer = os.path.join(work, "inference_rec")
    subprocess.check_call([sys.executable, os.path.join(paddleocr_repo, "tools", "export_model.py"),
                           "-c", config_yml, "-o", f"Global.pretrained_model={_fwd(best)}",
                           f"Global.save_inference_dir={_fwd(infer)}"], env=env)

    onnx_out = os.path.join(work, "rec.onnx")
    _paddle2onnx(["--model_dir", infer,
                  "--model_filename", "inference.pdmodel",
                  "--params_filename", "inference.pdiparams",
                  "--save_file", onnx_out, "--opset_version", "11"], env)
    return onnx_out
