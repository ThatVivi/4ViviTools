import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from PIL import Image
from build_dataset import merge_and_split

def _mk(d, n, pre):
    os.makedirs(os.path.join(d, "crops"), exist_ok=True)
    lines = []
    for i in range(n):
        rel = os.path.join("crops", f"{pre}_{i}.png")
        Image.new("RGB", (10, 10)).save(os.path.join(d, rel))
        lines.append(f"{rel}\t{i}")
    open(os.path.join(d, "rec_gt.txt"), "w").write("\n".join(lines) + "\n")

def test_merge_keeps_ratio_and_splits(tmp_path):
    synth = tmp_path / "synth"; real = tmp_path / "real"
    _mk(str(synth), 1000, "s"); _mk(str(real), 100, "r")
    out = tmp_path / "dataset"
    train, val = merge_and_split(str(synth), str(real), str(out), real_ratio=10, val_frac=0.1)
    assert train > 0 and val > 0
    assert (out / "train_list.txt").exists() and (out / "val_list.txt").exists()
