# Task #52 — Run synthetic RO-font rec fine-tune

**Status:** 🟡 in progress

## What it is
Train RO-specific recognition model. Biggest ceiling-raiser. Guide §4.

## Done so far
synth.py + patterns.py now RO-realistic (small sizes, outline/shadow/color, busy bg, HUD formats, Windows fonts). Pipeline: build_corpus.py -> synth.py -> train_export.py -> ONNX.

## Next / how to continue
NEEDS ATTENTION (your GPU): 1) run build_corpus.py with gamedata.json, 2) synth.py --count 100000, 3) train_export.py to fine-tune + export, 4) ship the new latin onnx + rebuild.

## Files
tools/ocr-train/synth.py, patterns.py, train_export.py
