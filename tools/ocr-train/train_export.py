import os, subprocess, sys, urllib.request, tarfile

PRETRAIN_URL = "https://paddleocr.bj.bcebos.com/PP-OCRv3/english/en_PP-OCRv3_rec_train.tar"

def _ensure_pretrained(work):
    dst = os.path.join(work, "pretrain")
    params = os.path.join(dst, "en_PP-OCRv3_rec_train", "best_accuracy.pdparams")
    if os.path.exists(params): return params[:-len(".pdparams")]
    os.makedirs(dst, exist_ok=True)
    tar = os.path.join(dst, "rec.tar")
    urllib.request.urlretrieve(PRETRAIN_URL, tar)
    with tarfile.open(tar) as t: t.extractall(dst)
    return params[:-len(".pdparams")]

def write_config(template, data_dir, use_gpu, pretrained, out_yml):
    s = open(template, encoding="utf-8").read()
    s = s.replace("__USE_GPU__", "true" if use_gpu else "false")
    s = s.replace("__DATA_DIR__", data_dir).replace("__PRETRAINED__", pretrained)
    open(out_yml, "w", encoding="utf-8").write(s)

def run(paddleocr_repo, config_yml, work):
    env = dict(os.environ)
    # paddle's generated protobuf is old; pure-python parsing tolerates any installed protobuf.
    env["PROTOCOL_BUFFERS_PYTHON_IMPLEMENTATION"] = "python"
    subprocess.check_call([sys.executable, os.path.join(paddleocr_repo, "tools", "train.py"),
                           "-c", config_yml], env=env)
    best = "./output/rec_ro/best_accuracy"
    infer = os.path.join(work, "inference_rec")
    subprocess.check_call([sys.executable, os.path.join(paddleocr_repo, "tools", "export_model.py"),
                           "-c", config_yml, "-o", f"Global.pretrained_model={best}",
                           f"Global.save_inference_dir={infer}"], env=env)
    onnx_out = os.path.join(work, "rec.onnx")
    subprocess.check_call(["paddle2onnx", "--model_dir", infer,
                           "--model_filename", "inference.pdmodel",
                           "--params_filename", "inference.pdiparams",
                           "--save_file", onnx_out, "--opset_version", "11"], env=env)
    return onnx_out
