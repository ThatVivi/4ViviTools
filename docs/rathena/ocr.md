# OCR — pipeline, dictionary correction, retraining

> Re-extract/build: OCR roles in `OcrReaderViewModel`; corrector in `src/4rVivi.Core/Game/OcrNameCorrector.cs`.

The OCR reads live game state from the screen (no memory addresses) and feeds `LiveStats`, which every
engine/shell reads. This is what replaces 4RTools/ro-tools' memory reading.

## Pipeline
1. **Capture** a frame (monitor or window).
2. Per user-placed **mark** (region + role):
   - **Bars** (`IsBar`) → fill % (HP/SP/EXP).
   - **Combined** (`HP / MaxHP`) → two ints `1234 / 5678`.
   - **Char box** (`IsChar`) → motion metric `CharMotion` (0–100) for posture/auto-stand.
   - **Numbers** → `ParseFirstInt`.
   - **Text** → recognized string → **dictionary correction** (new) → `LiveStats.SetText(role, value)`.
3. Recognition engine: **RapidOCR (ONNX)** with a RO-font-tuned model; tuning knobs in `OcrTuningConfig`
   (det thresholds, max side, **CpuThreads=8**).

## Dictionary correction (the overhaul)
`OcrNameCorrector` snaps a fuzzy text read to the nearest **real game string** from our embedded data,
per role — the highest-leverage accuracy win (OCR misreads → valid names). Built at startup from:
| Role | Dictionary source |
|------|-------------------|
| `ClassName` | `ClassCatalog` (all classes) |
| `Monster` | `mob_db` names |
| `MapName` | map list |
| `ItemName` | item names |
| `SkillName` | skill names |
Matching = normalized Levenshtein, accept if distance ≤ 35% of the longer length, else keep raw.
(`CharName` is the player's own name — not dictionary-correctable.)

### What this unlocks
- Read the **target monster** → snap to a real mob → can auto-fill the calculator enemy / show MVP info.
- Read the **map** → MVP-tracker context.
- Read the **class** → auto-set calculator/spammer class.
- Read **posture/state** → auto-stand (see [ro-tools-bot-and-states.md](ro-tools-bot-and-states.md)).

## Roles
`HP, MaxHP, SP, MaxSP, HpPercent, SpPercent, BaseLevel, JobLevel, Weight, MaxWeight, Zeny, CharName,
ClassName, MapName, Monster, Posture, ItemName, SkillName, …` plus combined `HP / MaxHP` etc.

## Retraining the recognition model (RO font)
The in-app **OCR Reader** can export training data; the Python trainer fine-tunes PaddleOCR → ONNX.
Steps (one-time, GPU recommended):
1. In the app's **OCR Reader**, place boxes over HP/SP/Name/etc. on a screenshot and **Export templates**
   (writes synthetic + your crops to the training folder).
2. Run the trainer (PaddleOCR 3.x):
   ```
   pip install "paddlepaddle-gpu" "nvidia-cudnn-cu12>=9.9,<10" paddle2onnx
   python tools/train.py -c configs/rec/PP-OCRv5/latin_PP-OCRv5_rec.yml \
       -o Global.epoch_num=15 Train.loader.batch_size_per_card=32
   ```
3. Export → ONNX:
   ```
   python tools/export_model.py -c <config> -o Global.pretrained_model=<best>
   paddle2onnx --model_dir inference --save_file latin_PP-OCRv5_rec_mobile_infer.onnx
   ```
4. Drop the `.onnx` into the app's `models/v5/` and **Reload** — `EngineInfo` shows the active model.
5. Tune `OcrTuning` (det thresholds / max side) in Settings if boxes mis-detect.

The dictionary correction means the model only has to get *close* — names snap to the real value, so a
modestly trained model is enough for reliable class/map/monster reads.
