#!/usr/bin/env python3
"""
train_all.py -- ONE command builds BOTH models for 4ViviTools, end to end.

Runs the three stages in order (GPU is used one stage at a time):
  [A] text OCR        -> run.py                (reads names/HP/SP/HUD/numbers in the RO font)
  [B] extract sprites -> extract_sprites.py    (decode GRF .spr -> icons/raw)
  [C] icon classifier -> build_icon_model.py   (icons + sprites -> icon_classifier.onnx)

Result = two shipped files:
  src/RapidOcrNet/models/v5/latin_PP-OCRv5_rec_mobile_infer.onnx   (text)
  src/RapidOcrNet/models/icons/icon_classifier.onnx                (icons + sprites)

Just run:
  python tools/ocr-train/train_all.py

Useful flags:
  --only CATS        sprite categories (default: all). e.g. --only monsters,jobs,homun,doram
  --text-epochs N    text OCR epochs (default 20)
  --icon-epochs N    classifier epochs (default 15)
  --variants N       augmented variants per icon/sprite (default 16)
  --skip-text        skip stage A (text already trained)
  --skip-sprites     skip stage B (sprites already extracted)
  --skip-icons       skip stage C
  --reuse-text-data  pass --skip-data --skip-corpus to run.py (reuse the rendered text set)
  --cpu              force CPU everywhere
"""
import argparse, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))

def run(label, args):
    print("\n" + "=" * 70 + "\n[train_all] %s\n" % label + "=" * 70, flush=True)
    env = dict(os.environ); env["NO_PAUSE"] = "1"   # children must not block train_all
    subprocess.check_call([sys.executable] + args, env=env)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--text-epochs", type=int, default=20)
    ap.add_argument("--icon-epochs", type=int, default=15)
    ap.add_argument("--variants", type=int, default=16)
    ap.add_argument("--skip-text", action="store_true")
    ap.add_argument("--skip-sprites", action="store_true")
    ap.add_argument("--skip-icons", action="store_true")
    ap.add_argument("--reuse-text-data", action="store_true")
    ap.add_argument("--cpu", action="store_true")
    a = ap.parse_args()

    if not a.skip_text:
        args = [os.path.join(HERE, "run.py"), "--epochs", str(a.text_epochs)]
        if a.reuse_text_data: args += ["--skip-data", "--skip-corpus"]
        if a.cpu: args += ["--cpu"]
        run("A) text OCR", args)
    else:
        print("[train_all] skip A (text OCR)")

    if not a.skip_sprites:
        args = [os.path.join(HERE, "extract_sprites.py")]
        if a.only: args += ["--only", a.only]
        run("B) extract sprites", args)
    else:
        print("[train_all] skip B (sprite extraction)")

    if not a.skip_icons:
        args = [os.path.join(HERE, "build_icon_model.py"),
                "--epochs", str(a.icon_epochs), "--variants", str(a.variants)]
        if a.cpu: args += ["--cpu"]
        run("C) icon + sprite classifier", args)
    else:
        print("[train_all] skip C (icon classifier)")

    print("\n[train_all] ALL DONE -> text + icon models installed under src/RapidOcrNet/models/")

if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback; traceback.print_exc()
    if not os.environ.get("NO_PAUSE"):
        try: input("\nFinished. Press Enter to close...")
        except Exception: pass
