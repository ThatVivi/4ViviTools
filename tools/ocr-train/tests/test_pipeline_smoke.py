import sys, os, json
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from PIL import Image
import synth, autolabel, build_dataset

def test_synth_to_dataset_pipeline(tmp_path):
    work = tmp_path
    fonts = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "fonts")
    synth.generate(str(work / "synth"), count=40, fonts_dir=fonts)
    (work / "imgs").mkdir(); Image.new("RGB", (800, 600), (0, 0, 0)).save(work / "imgs" / "a.png")
    tmpl = {"marks": [{"role": "HP", "x": 0.1, "y": 0.1, "w": 0.2, "h": 0.1, "isText": False, "isBar": False}]}
    (work / "template.json").write_text(json.dumps(tmpl))
    autolabel.label_folder(str(work / "imgs"), str(work / "template.json"), str(work / "real"), lambda im: "123/456")
    train, val = build_dataset.merge_and_split(str(work / "synth"), str(work / "real"), str(work / "dataset"))
    assert train > 0 and val > 0
    assert os.path.exists(work / "dataset" / "train_list.txt")
