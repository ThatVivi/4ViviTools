# OCR pipeline tuning + one-click custom-model training — design

Date: 2026-06-20
Owner: Vivi
Status: approved-pending-review

## 1. Goal

Make 4ViviTools' PaddleOCR (a) more accurate in real time via in-app inference tuning, and
(b) trainable to a custom RO model from inside the app with one click, mixing synthetic
RO-font data with the user's own screenshots. When training finishes, the new recognition
model drops into the app and is used immediately for real-time reads.

This spec covers sub-projects **SP3 (in-app inference tuning)** and **SP4 (offline one-click
training kit, triggered from the app)**. It does NOT cover the full 4RTools/ro-tools feature
merge (SP1/SP2/SP5) — that is a separate spec.

## 2. Decisions (locked)

- Training data: **mixed** — synthetic RO-font samples (bulk) + user real crops, ~10:1 ratio
  (per PaddleOCR fine-tune doc, to avoid single-scene overfit).
- Trainer host: **button inside the app** ("Train OCR" in OCR Reader) that shells out to a
  bundled Python trainer and streams progress.
- The app **creates an empty `tools/ocr-train/user_images/` folder**; the user drops real
  screenshots there. The button defaults to that folder (no path argument needed).
- The app **creates a `tools/ocr-train/reference/` folder** holding ONE reference screenshot
  plus `template.json` (role -> box rect). The user marks each field once (HP/MaxHP, SP/MaxSP,
  char name, class, zeny, weight, X/Y, char-motion box, EXP bars, HP/SP bars, etc.). The OCR
  Reader **exports its current calibrated marks** straight into this template (reuse of the
  existing marks system), so no separate marking tool is needed.
- **~30-40 real screenshots** is the agreed real seed. Each screenshot yields many field
  crops (HP, SP, levels, zeny, name, weight), so ~30-40 shots -> a few hundred real crops;
  synthetic generation supplies the bulk to clear the >=5,000 rec-sample bar.
- Fonts are supplied (bundled under `tools/ocr-train/fonts/`): Arial family, MS Sans Serif
  (`micross.ttf`), and Rix substitutes (Squirrel, KR Love Angels, Angel Love).
- Training uses **GPU + CPU** (GPU wheel auto-selected when CUDA is present; CPU otherwise).
- Engine model for the wider merge: shared (one source of truth). Not relevant to this spec.
- Only the **recognition** model is retrained for now. Detection stays stock PP-OCRv5 det.

## 3. Research basis (key facts driving the design)

- Rec fine-tune wants **>=5,000 samples**; synthetic generation is explicitly endorsed.
  Keep the dictionary unchanged. Start from PP-OCRv3 rec pretrained. **Remove GTC** (SAR
  branch overfits simple scenes). Single-GPU lr `[1e-4, 2e-5]`, batch 128 (scale down with
  batch). Detection (not used here) would need >=500, lr ~1e-4, batch 8.
- Training realistically wants a GPU; CPU works but is slow (hours for 5k+). Auto-pick GPU
  wheel if CUDA present, else CPU wheel.
- Output is a Paddle **inference model** -> `paddle2onnx` -> our runtime `models/v5/rec.onnx`.
- PPOCRLabel gives semi-auto labeling (auto-recognize -> correct -> exports `rec_gt.txt` +
  `crop_img`); CPU install is just `pip install paddlepaddle` (cpu wheel). Use `###` to mark
  unreadable crops as ignore.
- Real-time CPU perf levers: cap `cpu_threads`, **limit input image size**, PP-OCRv3 lighter
  than v4, optional `cpu_affinity` core cap. (OpenVINO export is faster on CPU but a heavy
  dependency — deferred, not in this spec.)

## 4. Architecture

Two tracks, loosely coupled through the shared `models/v5/` folder.

### Track A — in-app inference tuning (C#/.NET, ships in app)
Surface the proven PaddleOCR inference knobs in the OCR Reader and pass them through
`OcrService` -> `RapidOcrClient` -> `OcrServer` (the out-of-process worker that already runs
the ONNX models). Persist to `AppSettings`.

Exposed params (per-profile, with sane defaults):
- `DetDbThresh` (0.3), `DetDbBoxThresh` (0.6), `DetDbUnclipRatio` (1.5), `UseDilation` (false)
  — detection sensitivity, improves small-HUD-text boxes.
- `RecImageHeight` (48) and max width — rec input shape.
- `CpuThreads` (default = min(4, cores)) and `MaxImageSide` (downscale cap) — real-time cost.
- `DropScore` (0.5) — discard low-confidence reads.
- `NumericRoles` — for roles known to be digits (HP, SP, levels, zeny, weight), restrict
  accepted characters to `0-9 / .` in post-processing (cheap accuracy win, no retrain needed).

These flow to the OcrServer as CLI/stdin args; OcrServer applies them when building the
RapidOcr options. No new ONNX needed for Track A.

### Track B — one-click training kit (Python, `tools/ocr-train/`, invoked by app)
A self-contained folder shipped in the repo, run by the app button or by hand.

Components (each a single-purpose unit):
- `B1 run.py` — orchestrator. Steps: env-check -> install Paddle (GPU/CPU autodetect) ->
  build dataset -> train -> export -> paddle2onnx -> copy `rec.onnx` into `models/v5/` ->
  print DONE. Streams stdout the app reads.
- `B2 synth.py` — synthetic generator. Inputs: bundled fonts in `tools/ocr-train/fonts/`
  (Arial, MS Sans Serif, Squirrel, KR Love Angels, Angel Love), value-pattern templates
  (HP `1234/5678`, base/job level, zeny, weight, char names). Renders crops at HUD sizes +
  light noise/scale jitter, writes `crop_img/*.png` + `rec_gt.txt`. Target: >=5,000.
- `B3 autolabel.py` — uses `reference/template.json` to crop the **same role box from the
  same coordinates** in every screenshot in `tools/ocr-train/user_images/`, so each crop is a
  known role. It reads each crop with the current `rec.onnx` and constrains the label to that
  role's expected format (numeric `0-9 / .` for HP/SP/level/zeny/weight; free text for name).
  Writes `rec_gt.txt`. Crops it can't read confidently are marked `###` (ignored). Optional
  `--ppocrlabel` opens PPOCRLabel for the user to spot-fix before training. Bar roles (HP/SP/
  EXP bars, char-motion) are NOT text and are excluded from rec training — the template's bar
  boxes feed the app's fill-percent calibration instead.
- `B4 build_dataset.py` — merges synth + real crops at ~10:1, dedupes, splits train/val
  (gen_ocr_train_val_test style), writes label lists + the rec config (PP-OCRv3, GTC removed,
  lr/batch per research, dict unchanged).
- `B5 train_export.py` — `paddle.../train.py -c config.yml`, then export inference model,
  then `paddle2onnx` -> `rec.onnx`. On any failure, leave the existing model untouched.

### Glue (C#)
- `C1` "Train OCR" button in OCR Reader -> ensures `tools/ocr-train/user_images/` exists,
  opens it for the user to drop ~30-40 screenshots, then launches `python
  tools/ocr-train/run.py` (defaults to that folder) in a child process; a progress panel
  tails stdout; Cancel kills it.
- `C2` model hot-reload: after DONE, the app restarts the OcrServer child so it loads the new
  `models/v5/rec.onnx`; `LastEngine` confirms PaddleOCR is live.

## 5. Data flow

Reference template (role->box) + user screenshots + bundled fonts ->
  B2 synth crops + B3 template-cropped, role-labeled real crops ->
  B4 merged/split dataset + config ->
  B5 train (GPU if CUDA else CPU) -> Paddle inference model -> paddle2onnx -> models/v5/rec.onnx ->
  C2 OcrServer reload -> real-time reads use the new model.

## 6. Required inputs / dependencies

- **Fonts: supplied and bundled** under `tools/ocr-train/fonts/` (Arial, MS Sans Serif,
  Squirrel, KR Love Angels, Angel Love). If a server uses a different HUD font, drop it in
  that folder and the generator picks it up.
- **Reference template**: one reference screenshot + `template.json` (role -> box), produced
  by exporting the OCR Reader marks. Required so the trainer crops the right field from each
  training screenshot. All training screenshots must share the reference's UI layout/resolution.
- Python 3.10+ with pip; the trainer installs `paddlepaddle`(-gpu), `paddleocr`, `paddle2onnx`,
  Pillow on first run. ~2-4 GB download.
- CUDA optional. Present -> GPU wheel (fast). Absent -> CPU wheel (works, slow).

## 7. Error handling

- No Python/pip -> button shows a one-line install hint, does nothing destructive.
- Paddle install / train / export failure -> keep the old `rec.onnx`; surface the error tail.
- ONNX parity check: B5 runs old vs new model on a small held-out set; if new is worse on the
  numeric roles, it warns and keeps the old model unless `--force`.
- Cancel mid-train -> kill child, old model intact.

## 8. Testing / verification

- Synthetic smoke: generate ~50 samples, run 5 train iters, export, confirm `rec.onnx`
  is produced and loads in OcrServer.
- Accuracy check: held-out synthetic + a few real crops; report char accuracy old vs new.
- App integration: press Train OCR with a tiny set, confirm progress streams, model reloads,
  `LastEngine == PaddleOCR`, and OCR Reader live values still parse.
- Track A: toggling each inference param changes the read as expected on a fixed screenshot.

## 9. Out of scope (separate specs)

- Full 4RTools/ro-tools feature merge (SP1 macros/buffs, SP2 game-state autos, SP5 dashboard).
- Detection-model retraining (det stays stock).
- OpenVINO runtime swap.

## 10. Risks

- Real font fidelity: synthetic quality hinges on the bundled fonts matching the client; if a
  server's HUD font differs, accuracy drops until that font is added to `fonts/`.
- Small real set: ~30-40 screenshots is a light real signal; the custom model mainly sharpens
  digits/short HUD text and won't be transformative. Adding more screenshots later improves it.
- Template alignment: the reference template assumes every training screenshot uses the same
  HUD layout and resolution as the reference. Shots at a different window size/scale won't align
  the boxes; mitigate by capturing all training shots at one fixed resolution (or add a template
  per resolution).
- CPU training time: hours for 5k+. Acceptable per user (one-click, run and wait).
- Paddle footprint: large install; one-time, gated behind the button.
