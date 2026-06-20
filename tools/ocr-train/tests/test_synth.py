import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from synth import generate

def test_generate_makes_crops_and_labels(tmp_path):
    out = tmp_path / "synth"
    fonts = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "fonts")
    n = generate(str(out), count=20, fonts_dir=fonts)
    assert n == 20
    gt = (out / "rec_gt.txt").read_text().strip().splitlines()
    assert len(gt) == 20
    img0, label0 = gt[0].split("\t")
    assert (out / img0).exists() and len(label0) > 0
