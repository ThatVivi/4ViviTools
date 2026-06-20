import os, random, shutil

def _read(gt):
    return [l for l in open(gt, encoding="utf-8").read().splitlines() if "\t" in l]

def merge_and_split(synth_dir, real_dir, out_dir, real_ratio=10, val_frac=0.1):
    os.makedirs(os.path.join(out_dir, "crops"), exist_ok=True)
    rg = os.path.join(real_dir, "rec_gt.txt"); sg = os.path.join(synth_dir, "rec_gt.txt")
    real = _read(rg) if os.path.exists(rg) else []
    synth = _read(sg) if os.path.exists(sg) else []
    cap = len(real) * real_ratio if real else len(synth)
    if real and cap > 0: synth = synth[:cap]
    merged = []
    for src_dir, lines, pre in [(synth_dir, synth, "s"), (real_dir, real, "r")]:
        for j, ln in enumerate(lines):
            rel, label = ln.split("\t", 1)
            newrel = os.path.join("crops", f"{pre}_{j:06d}.png")
            shutil.copy(os.path.join(src_dir, rel), os.path.join(out_dir, newrel))
            merged.append(f"{newrel}\t{label}")
    random.shuffle(merged)
    k = max(1, int(len(merged) * val_frac)) if merged else 0
    val, train = merged[:k], merged[k:]
    open(os.path.join(out_dir, "train_list.txt"), "w", encoding="utf-8").write("\n".join(train) + "\n")
    open(os.path.join(out_dir, "val_list.txt"), "w", encoding="utf-8").write("\n".join(val) + "\n")
    return len(train), len(val)
