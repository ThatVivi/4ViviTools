#!/usr/bin/env python3
"""
build_icon_model.py -- ICON RECOGNIZER via metric learning (ArcFace embedding + nearest-neighbor).

Why not a 12k-way softmax: we have ONE reference image per icon. A classifier over 12k classes
caps out (~33% before). Metric learning instead learns a 256-D embedding so that augmented views
of an icon land near its clean reference. At runtime: embed the screen crop, cosine-match it to a
bank of clean reference embeddings. New icons need no retrain -- just embed + append to the bank.

Sequence:
  [1] extract icons  -> icons/raw/<class>/0.png            (items + skills from iconpack.zip, + GRF sprites)
  [2] augment        -> icons/train + icons/val (each icon at many sizes / backgrounds / flips / blur)
  [3] train          -> MobileNetV3-Large -> 256-D embedding, ArcFace margin loss (paddle, FP16)
  [4] reference bank -> embed every clean icon -> models/icons/icon_refs.bin (raw f32, C#-read) (+ labels.txt)
                        embedder weights -> models/icons/icon_embedder.pdparams (ONNX via convert_in_venv.bat)

Val metric = 1-NN retrieval: embed val crop, nearest clean reference, is it the right icon.
Double-click to run. Resumable. Flags: --variants N(24) --epochs N(40) --img 64 --emb 256 --skip-data --cpu
"""
import argparse, glob, json, os, random, shutil, sys, zipfile
from PIL import Image, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ICONPACK = os.path.join(HERE, "..", "..", "src", "4rVivi.App", "Assets", "iconpack.zip")
ICONS = os.path.join(HERE, "icons")
RAW = os.path.join(ICONS, "raw")
OUT_DIR = os.path.join(HERE, "..", "..", "src", "RapidOcrNet", "models", "icons")
BACKBONE = "arcface_mnv3l_v2"   # tag: changing this invalidates old checkpoints
AUGMETA = os.path.join(ICONS, "aug_meta.json")

def log(m): print(m, flush=True)

# ---------------------------------------------------------------- [1] extract
def extract_icons():
    os.makedirs(RAW, exist_ok=True)
    z = zipfile.ZipFile(ICONPACK)
    names = [n for n in z.namelist() if n.lower().endswith(".png")]
    classes = []
    for n in names:
        grp, fn = n.split("/", 1) if "/" in n else ("misc", n)
        if grp.lower().startswith("map"):   # maps removed: they conflict with monster recognition
            continue
        cls = grp + "__" + os.path.splitext(os.path.basename(fn))[0]
        d = os.path.join(RAW, cls); os.makedirs(d, exist_ok=True)
        with z.open(n) as f, open(os.path.join(d, "0.png"), "wb") as o:
            o.write(f.read())
        classes.append(cls)
    log("[1] extracted %d icons -> icons/raw" % len(classes))
    return sorted(set(classes))

# ---------------------------------------------------------------- [2] augment
def _bg(size):
    r = random.random()
    if r < 0.34: return Image.new("RGB", size, random.choice([(20,20,28),(0,0,0),(40,40,40),(28,30,40),(60,40,30),(30,40,60)]))
    if r < 0.60: return Image.new("RGB", size, (255,255,255))
    im = Image.new("RGB", size); px = im.load()
    base = random.randint(0,90)
    for y in range(size[1]):
        for x in range(size[0]):
            v = max(0, min(255, base + random.randint(-25,25))); px[x,y]=(v,v,v)
    return im

def _variant(icon_rgba, img):
    s = random.randint(16, 96)
    ic = icon_rgba.resize((s, s), Image.LANCZOS)
    if random.random() < 0.5:
        ic = ic.rotate(random.uniform(-15, 15), expand=True, resample=Image.BICUBIC)
    if random.random() < 0.5:
        ic = ic.transpose(Image.FLIP_LEFT_RIGHT)
    canvas = _bg((img, img))
    ox = random.randint(0, max(0, img - ic.width)); oy = random.randint(0, max(0, img - ic.height))
    canvas.paste(ic, (ox, oy), ic if ic.mode == "RGBA" else None)
    if random.random() < 0.5: canvas = ImageEnhance.Brightness(canvas).enhance(random.uniform(0.65, 1.35))
    if random.random() < 0.4: canvas = ImageEnhance.Color(canvas).enhance(random.uniform(0.6, 1.4))
    if random.random() < 0.3: canvas = ImageEnhance.Contrast(canvas).enhance(random.uniform(0.7, 1.3))
    if random.random() < 0.25: canvas = canvas.filter(ImageFilter.GaussianBlur(random.uniform(0.4, 1.1)))
    return canvas

def augment(classes, variants, img, val_frac=0.1):
    for sub in ("train", "val"):
        shutil.rmtree(os.path.join(ICONS, sub), ignore_errors=True)
        os.makedirs(os.path.join(ICONS, sub), exist_ok=True)
    labels = {c: i for i, c in enumerate(classes)}
    for mk in ("labels.json", "labels.txt", "train_list.txt", "val_list.txt", "aug_meta.json"):
        try: os.remove(os.path.join(ICONS, mk))
        except OSError: pass
    tr, va = [], []
    for c in classes:
        pngs = sorted(glob.glob(os.path.join(RAW, c, "*.png")))
        if not pngs: continue
        for k in range(variants):
            try: base = Image.open(pngs[k % len(pngs)]).convert("RGBA")   # cycle the class's poses
            except Exception: continue
            im = _variant(base, img)
            sub = "val" if random.random() < val_frac else "train"
            rel = os.path.join(sub, "%s_%02d.png" % (c, k))
            im.save(os.path.join(ICONS, rel))
            (va if sub == "val" else tr).append("%s\t%d" % (rel, labels[c]))
    open(os.path.join(ICONS, "train_list.txt"), "w", encoding="utf-8").write("\n".join(tr) + "\n")
    open(os.path.join(ICONS, "val_list.txt"), "w", encoding="utf-8").write("\n".join(va) + "\n")
    json.dump(labels, open(os.path.join(ICONS, "labels.json"), "w"))
    open(os.path.join(ICONS, "labels.txt"), "w", encoding="utf-8").write(
        "\n".join("%d\t%s" % (i, c) for c, i in labels.items()) + "\n")
    json.dump({"classes": len(classes), "variants": variants, "img": img}, open(AUGMETA, "w"))
    log("[2] augmented: %d train / %d val over %d classes (%d variants)" % (len(tr), len(va), len(classes), variants))

# ---------------------------------------------------------------- model
def build_embedder(emb):
    from paddle.vision.models import mobilenet_v3_large
    # Correct architecture (final Linear -> emb), then warm-start the BACKBONE from ImageNet by
    # copying only shape-matching tensors (the final head differs in size -> stays random init).
    net = mobilenet_v3_large(num_classes=emb)
    try:
        pre = mobilenet_v3_large(pretrained=True)
        sd, psd = net.state_dict(), pre.state_dict()
        copied = 0
        for k in sd:
            if k in psd and tuple(psd[k].shape) == tuple(sd[k].shape):
                sd[k] = psd[k]; copied += 1
        net.set_state_dict(sd)
        log("[backbone] warm-started %d/%d tensors from ImageNet (final head random)" % (copied, len(sd)))
    except Exception as e:
        log("[warn] pretrained backbone unavailable (%s) -> scratch init" % type(e).__name__)
    return net

class _ArcFace:
    pass

# ---------------------------------------------------------------- [3]+[4]
def train_export(classes, img, epochs, use_gpu, emb):
    import numpy as np
    import paddle
    import paddle.nn as nn
    import paddle.nn.functional as F
    from paddle.io import Dataset, DataLoader
    paddle.set_device("gpu" if use_gpu else "cpu")
    num_classes = len(classes)

    class DS(Dataset):
        def __init__(self, listfile):
            self.rows = [l.split("\t") for l in open(listfile, encoding="utf-8").read().splitlines() if "\t" in l]
        def __len__(self): return len(self.rows)
        def __getitem__(self, i):
            rel, lab = self.rows[i]
            im = Image.open(os.path.join(ICONS, rel)).convert("RGB").resize((img, img))
            x = (np.asarray(im, "float32") / 255.0 - 0.5) / 0.5
            return x.transpose(2, 0, 1), np.int64(int(lab))

    class ArcFace(nn.Layer):
        def __init__(self, n, d, s=30.0, m=0.50):
            super().__init__()
            self.W = self.create_parameter([n, d], default_initializer=nn.initializer.XavierNormal())
            self.s, self.m = s, m
        def forward(self, e, label):
            e = e.astype("float32")
            ef = F.normalize(e, axis=1); Wn = F.normalize(self.W, axis=1)
            cos = paddle.matmul(ef, Wn, transpose_y=True).clip(-1 + 1e-6, 1 - 1e-6)
            if self.m <= 1e-6:
                return self.s * cos
            tgt = paddle.cos(paddle.acos(cos) + self.m)
            oh = F.one_hot(label, num_classes=self.W.shape[0])
            return self.s * (oh * tgt + (1 - oh) * cos)

    tr = DataLoader(DS(os.path.join(ICONS, "train_list.txt")), batch_size=128, shuffle=True,
                    drop_last=True, num_workers=0)
    va = DataLoader(DS(os.path.join(ICONS, "val_list.txt")), batch_size=256, num_workers=0)
    net = build_embedder(emb)
    head = ArcFace(num_classes, emb, m=0.0)        # margin ramps up (below)
    params = net.parameters() + head.parameters()
    steps = max(1, len(tr))
    cos = paddle.optimizer.lr.CosineAnnealingDecay(learning_rate=5e-4, T_max=epochs * steps)
    sched = paddle.optimizer.lr.LinearWarmup(cos, warmup_steps=max(1, steps), start_lr=1e-6, end_lr=5e-4)
    opt = paddle.optimizer.Adam(sched, parameters=params, weight_decay=5e-4)
    scaler = None                                  # ArcFace acos is unstable under FP16 -> train fp32
    MARGIN_EPOCHS = 8

    ck = os.path.join(ICONS, "ckpt"); os.makedirs(ck, exist_ok=True)
    ep_path = os.path.join(ck, "embedder.pdparams"); hp = os.path.join(ck, "head.pdparams")
    op = os.path.join(ck, "opt.pdopt"); meta = os.path.join(ck, "meta.json")
    best_path = os.path.join(ck, "embedder_best.pdparams")
    start = 0; best = 0.0
    if os.path.exists(ep_path) and os.path.exists(meta):
        try:
            m = json.load(open(meta))
            if m.get("classes") == num_classes and m.get("backbone") == BACKBONE and m.get("emb") == emb:
                net.set_state_dict(paddle.load(ep_path))
                try: head.set_state_dict(paddle.load(hp))
                except Exception: pass
                try: opt.set_state_dict(paddle.load(op))
                except Exception: pass
                start = int(m.get("epoch", 0)); best = float(m.get("best", 0.0))
                log("[resume] %s -> from epoch %d (best retrieval %.3f)" % (BACKBONE, start + 1, best))
            else:
                log("[fresh] config changed -> training %s from scratch" % BACKBONE)
        except Exception as e:
            log("[resume] ignoring bad checkpoint: %s" % e)

    # clean reference tensors (one per class, in label order) for the retrieval metric + bank
    def embed_refs():
        net.eval(); vecs = []
        idx2cls = {i: c for c, i in [(c, i) for i, c in enumerate(classes)]}
        batch = []; order = []
        out = np.zeros((num_classes, emb), "float32")
        def flush():
            if not batch: return
            x = paddle.to_tensor(np.stack(batch))
            with paddle.no_grad():
                e = F.normalize(net(x).astype("float32"), axis=1).numpy()
            for j, oi in enumerate(order): out[oi] = e[j]
            batch.clear(); order.clear()
        for i in range(num_classes):
            try:
                im = Image.open(os.path.join(RAW, idx2cls[i], "0.png")).convert("RGB").resize((img, img))
            except Exception:
                continue
            x = (np.asarray(im, "float32") / 255.0 - 0.5) / 0.5
            batch.append(x.transpose(2, 0, 1)); order.append(i)
            if len(batch) == 256: flush()
        flush()
        return out  # [N, emb] normalized

    def retrieval_acc(bank):
        net.eval(); cor = tot = 0
        B = paddle.to_tensor(bank)  # [N, emb]
        with paddle.no_grad():
            for x, y in va:
                e = F.normalize(net(x).astype("float32"), axis=1)
                sim = paddle.matmul(e, B, transpose_y=True)  # [b, N]
                pred = sim.argmax(1)
                cor += int((pred == y).sum()); tot += int(y.shape[0])
        return cor / max(1, tot)

    for ep in range(start, epochs):
        net.train(); head.train()
        head.m = 0.5 * min(1.0, (ep + 1) / float(MARGIN_EPOCHS))   # ramp ArcFace margin 0 -> 0.5
        for bi, (x, y) in enumerate(tr):
            e = net(x)
            logits = head(e, y)
            loss = paddle.nn.functional.cross_entropy(logits, y)
            loss.backward(); opt.step()
            opt.clear_grad(); sched.step()
            if bi % 50 == 0:
                log("  ep %d/%d step %d loss %.4f lr %.5f m %.2f" % (ep+1, epochs, bi, float(loss), opt.get_lr(), head.m))
        bank = embed_refs()
        acc = retrieval_acc(bank)
        log("[3] epoch %d retrieval@1 %.3f" % (ep+1, acc))
        paddle.save(net.state_dict(), ep_path); paddle.save(head.state_dict(), hp); paddle.save(opt.state_dict(), op)
        if acc >= best:
            best = acc; paddle.save(net.state_dict(), best_path)
            np.save(os.path.join(ck, "icon_refs.npy"), bank)   # bank for the best epoch
        json.dump({"epoch": ep+1, "classes": num_classes, "backbone": BACKBONE, "emb": emb, "best": best},
                  open(meta, "w"))
        log("[ckpt] saved epoch %d (best %.3f) -> safe to quit; double-click again to resume" % (ep+1, best))

    # ----- export best embedder + reference bank
    os.makedirs(OUT_DIR, exist_ok=True)
    try: os.remove(os.path.join(OUT_DIR, "icon_classifier.pdparams"))  # remove dead classifier weights
    except OSError: pass
    if os.path.exists(best_path):
        try: net.set_state_dict(paddle.load(best_path)); log("[4] exporting best embedder (retrieval %.3f)" % best)
        except Exception: pass
    # MULTI-reference bank: embed EVERY pose png per class so a monster is matched from any direction.
    def embed_all_refs():
        net.eval(); idx2cls = {i: c for i, c in enumerate(classes)}
        vecs = []; names = []; batch = []; order = []
        def flush():
            if not batch: return
            x = paddle.to_tensor(np.stack(batch))
            with paddle.no_grad():
                e = F.normalize(net(x).astype("float32"), axis=1).numpy()
            for j, nm in enumerate(order): vecs.append(e[j]); names.append(nm)
            batch.clear(); order.clear()
        for i in range(num_classes):
            c = idx2cls[i]
            for p in sorted(glob.glob(os.path.join(RAW, c, "*.png"))):
                try: im = Image.open(p).convert("RGB").resize((img, img))
                except Exception: continue
                xa = (np.asarray(im, "float32") / 255.0 - 0.5) / 0.5
                batch.append(xa.transpose(2, 0, 1)); order.append(c)
                if len(batch) == 256: flush()
        flush()
        return (np.stack(vecs).astype("float32") if vecs else np.zeros((0, emb), "float32")), names
    bank, bank_names = embed_all_refs()
    bank = np.ascontiguousarray(bank, dtype="float32")          # [N, emb], L2-normalized, one row per pose
    with open(os.path.join(OUT_DIR, "icon_refs.bin"), "wb") as f:
        f.write(bank.tobytes())
    with open(os.path.join(OUT_DIR, "labels.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join("%d\t%s" % (i, n) for i, n in enumerate(bank_names)) + "\n")
    paddle.save(net.state_dict(), os.path.join(OUT_DIR, "icon_embedder.pdparams"))
    json.dump({"emb": int(emb), "img": int(img), "n": int(bank.shape[0]), "backbone": BACKBONE},
              open(os.path.join(OUT_DIR, "icon_meta.json"), "w"))
    log("[4] saved reference bank (%dx%d) + embedder weights -> models/icons/" % (bank.shape[0], emb))
    # ONNX export usually fails in the polluted global env; do it via convert_in_venv.bat (attempt C).
    try:
        spec = [paddle.static.InputSpec([None, 3, img, img], "float32", "x")]
        paddle.onnx.export(net, os.path.join(OUT_DIR, "icon_embedder"), input_spec=spec, opset_version=11)
        if os.path.getsize(os.path.join(OUT_DIR, "icon_embedder.onnx")) > 1024:
            log("[4] ONNX embedder exported -> models/icons/icon_embedder.onnx")
    except Exception as e:
        log("[4] ONNX export skipped here (%s). Run convert_in_venv.bat to make icon_embedder.onnx." % type(e).__name__)

def has_cuda():
    import subprocess
    try: return subprocess.call(["nvidia-smi"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) == 0
    except Exception: return False

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--variants", type=int, default=24)
    ap.add_argument("--epochs", type=int, default=40)
    ap.add_argument("--img", type=int, default=64)
    ap.add_argument("--emb", type=int, default=256)
    ap.add_argument("--skip-data", action="store_true")
    ap.add_argument("--cpu", action="store_true")
    a = ap.parse_args()
    use_gpu = has_cuda() and not a.cpu
    log("device: %s" % ("GPU" if use_gpu else "CPU"))

    extract_icons()
    raw_classes = sorted(os.path.basename(d) for d in glob.glob(os.path.join(RAW, "*")) if os.path.isdir(d))
    lp = os.path.join(ICONS, "labels.json")
    have = json.load(open(lp)) if os.path.exists(lp) else {}
    am = json.load(open(AUGMETA)) if os.path.exists(AUGMETA) else {}
    data_ready = (os.path.exists(os.path.join(ICONS, "train_list.txt"))
                  and bool(glob.glob(os.path.join(ICONS, "train", "*.png")))
                  and len(have) == len(raw_classes)
                  and am.get("variants") == a.variants and am.get("img") == a.img)
    if a.skip_data or data_ready:
        log("[1-2] dataset matches %d classes / %d variants -> reuse + resume" % (len(raw_classes), a.variants))
    else:
        log("[1-2] building dataset for %d classes, %d variants each" % (len(raw_classes), a.variants))
        augment(raw_classes, a.variants, a.img)
    classes = [c for c, _ in sorted(json.load(open(lp)).items(), key=lambda kv: kv[1])]
    train_export(classes, a.img, a.epochs, use_gpu, a.emb)

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
