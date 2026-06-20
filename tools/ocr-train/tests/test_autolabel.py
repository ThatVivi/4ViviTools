import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from PIL import Image
from autolabel import crop_roles

def test_crop_roles_uses_normalized_boxes(tmp_path):
    img = Image.new("RGB", (1000, 500), (0, 0, 0)); p = tmp_path / "shot.png"; img.save(p)
    tmpl = {"marks": [{"role": "HP", "x": 0.1, "y": 0.2, "w": 0.2, "h": 0.1, "isText": False, "isBar": False}]}
    crops = crop_roles(str(p), tmpl)
    assert crops[0]["role"] == "HP"
    assert crops[0]["box"] == (100, 100, 300, 150)
