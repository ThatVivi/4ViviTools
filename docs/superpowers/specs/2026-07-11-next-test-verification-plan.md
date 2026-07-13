# 4ViviTools — Next-Test Verification & Iteration Plan
**Date:** 2026-07-11 · **Depends on:** `2026-07-11-vision-wiring-and-detector-fix-plan.md` (T1–T6)
**State:** Codex implemented T1–T4 + input routing; build green, 48/48. **Not yet verified against a live pipeline.**
**Purpose:** prove the flicker/too-many-boxes/no-attack fixes actually took effect, decide what to do next per outcome, then move to the model phase. Tests don't exercise the live loop — the log does.

---

## Phase 0 — Pre-flight config (do exactly once, before the test run)

Per the implementer's note, confirm the app starts in the correct state:

1. **Run the current build, elevated:**
   `D:\vs code clone 4rtool\4ViviTools\artifacts\4ViviTools-latest-release-gpu\4rVivi.exe` (Run as administrator).
2. **OCR tab → `Monitor capture` is UNCHECKED** (client/window capture is the new default). If it's checked from an old saved profile, uncheck it once and Save.
3. **Smart Bot → Input method = `VIIPER virtual USB`.** If a saved profile kept an old backend, pick VIIPER once and Save. *(This is a settings selection only — it does not change the verification below.)*
4. **OCR runtime = `CUDA`.** Confirm the status line reads `engine: PaddleOCR runtime CUDA` (not CPU, not "worker unavailable").
5. **Delete/short-circuit any stale profile** that predates these changes so old defaults don't leak in. If unsure, create a fresh profile named `verify-0711`.

**Gate:** do not start the test until the OCR status shows CUDA and `Monitor capture` is unchecked.

---

## Phase 1 — Capture one clean evidence log

1. Fully close any old elevated instance first (a stale worker mark forces the CPU/Windows-OCR fallback).
2. Start the app, attach to the client, enable **OCR + overlay** and let it run **~60 seconds standing among monsters** (Skeleton/Poporing/etc.), then **~60 seconds walking**. This exercises both the "many mobs" and "moving/re-pin" cases.
3. Enable Smart Bot for ~30 seconds so target-selection lines are logged.
4. Stop, close, and grab the fresh log:
   `C:\Users\Vivi\AppData\Roaming\Claude\local-agent-mode-sessions\...\uploads\DebugTrace.log`

Keep this log labeled `DebugTrace-0711-run2.log` so we can diff against the previous (broken) run.

---

## Phase 2 — Verification battery (the money checks)

Run each grep on the **new** log. Each maps to a task and has an explicit pass criterion and a "what it means if it fails."

### Check A — T1 threshold frozen
```bash
grep -oE 'minScore=[0-9.]+' DebugTrace.log | sort -u
```
- **PASS:** exactly **one** value (expect `minScore=0.30`).
- **FAIL (multiple values):** the auto-formula still recomputes per frame → flicker source not removed. Reopen T1: the "Auto" checkbox must *set* the constant, not *call a function each tick*.

### Check B — T3 client capture
```bash
grep -oE 'mode=[a-z]+' DebugTrace.log | sort | uniq -c
grep -oE 'clientCoords=(True|False)' DebugTrace.log | sort | uniq -c
```
- **PASS:** `mode=client` on ~100 %, `clientCoords=False` count **== 0**.
- **FAIL (any False / still monitor):** capture didn't switch. Bot will keep ignoring ~a third of frames → aimless walk. Reopen T3.

### Check C — T2 confirmation gate (most likely under-done)
```bash
# raw must sometimes exceed entities (tentative boxes withheld until confirmed)
grep -oE 'raw=[0-9]+ entities=[0-9]+' DebugTrace.log \
 | awk -F'[= ]' '{r=$2;e=$4; tot++; if(r>e)g++; if(r<e)bad++}
   END{printf "frames=%d  raw>entities=%d  raw<entities=%d => %s\n",tot,g,bad,(bad?"FAIL(bad)":(g?"OK":"SUSPECT: raw==entities always"))}'
```
- **PASS:** `raw>entities` on a meaningful share, never `raw<entities`.
- **SUSPECT (`raw==entities` always):** `entities` is still built from raw detections, not confirmed tracks. This is the "too many boxes" root. Reopen T2 — `LiveScene.Entities` must be `tracks.Where(Visible||LostGrace)`, and *attackable* must require `Visible && Misses==0 && BestScore>=0.55`.

### Check D — attackable is a real subset
```bash
grep -oE 'entities=[0-9]+ .*attackable=[0-9]+' DebugTrace.log | sort | uniq -c | head
```
- **PASS:** `attackable` ≤ `entities`, and is 0 when only tentative/lost tracks exist.
- **FAIL (attackable==entities always):** the 0.55 attack gate isn't applied → bot clicks unconfirmed/ghost boxes. Reopen T2b.

### Check E — overlay/bot share truth (visual)
- Overlay shows **yellow "not attackable"** boxes for low/tentative tracks and **green** only for attackable ones. If every box looks identical, T4 didn't land (overlay still drawing raw boxes).

### Check F — Smart Bot locks a track
```bash
grep -oE 'trk#[0-9]+' DebugTrace.log | sort | uniq -c | sort -rn | head
```
- **PASS:** a few track ids dominate (the bot *held* a lock across many frames).
- **FAIL (every id appears once):** still "nearest box per frame," no hysteresis. Implement T6 lock-by-TrackId.

---

## Phase 3 — Decision tree (what to do with the result)

```
Checks A + B + C + D all PASS
   └─ YES → flicker + too-many-boxes + ignored-frames are fixed at the pipeline level.
             Proceed to Phase 4 (T5 naming, T6 lock) then Phase 5 (model).
   └─ NO  → fix ONLY the failing task, rebuild, re-run Phase 1–2. Do NOT start model work
             on a broken pipeline (you'd train around a bug). One failing check = one reopened task.
```

Paste back to the implementer, per failure:
- Check A fail → "minScore still varies: `<the sort -u output>` — freeze it."
- Check C suspect → "raw==entities on all frames — entities must be confirmed tracks only."
- Check F fail → "no track lock — bot re-picks nearest each frame; add TrackId hysteresis (T6)."

---

## Phase 4 — After the pipeline verifies (T5 + T6)

Only once Checks A–D pass:
1. **T6 target lock** (if Check F failed): stick to a `TrackId` until it dies/`Removed`; log the locked id.
2. **T5 per-track naming:** icon vote keyed by `trk{id}`; map-focus (`src/4rVivi.Core/Data/map_mobs.json`) biases the *name* only when the icon score is low, never creates/deletes a box. Verify: two different mobs on screen keep two stable distinct `#id name` labels; unknown map keeps generic `Monster` instead of guessing.

Re-run Phase 1–2 after each to confirm no regression on Checks A–D.

---

## Phase 5 — Model phase (root-cause, do last)

Training comes **after** wiring is proven, exactly as agreed. Sequence:

1. **Collect real frames** into `tools/ocr-train/real_frames/`; pre-label with `label_real.py`; hand-correct; move into `yolo_real/{images,labels}/train`.
2. **Mine hard negatives:** drop known false-positive frames into `tools/ocr-train/false_positive_frames/`; run `mine_hard_negatives.py` (empty-label backgrounds, keep at 10–20 % of the set).
3. **Retrain + export:**
   ```bash
   cd /d "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train"
   python train_yolo.py --data yolo_real\data.yaml --model yolo11n.pt --epochs 60 --imgsz 640 --batch 16
   python export_onnx.py --weights yolo_real\runs\entity\weights\best.pt ^
     --out ..\..\src\RapidOcrNet\models\yolo\entity.onnx
   ```
4. **Prove calibration before shipping:**
   ```bash
   python check_calibration.py
   ```
   **Ship gate:** ≥80 % of true-positive detections score ≥0.55 and the sub-0.30 mass is clearly junk (bimodal split). If not, add more real data — do NOT lower thresholds to hide it. Wire this into the existing "refuse to ship a bad detector" floor in `RUN_OVERNIGHT_YOLO_2060S.bat` / `RUN_EVERYTHING_2060S.bat`.
5. Re-run Phase 1–2 with the new `entity.onnx` — Checks A–D must still pass, and `raw=0` frames should drop sharply (model now confident on real frames).

---

## Phase 6 — Input observability (light, not a redesign)

Independent of which backend delivers input, make the routing **legible** so failures aren't silent:
1. Every click attempt logs the chosen backend and its result, e.g. `[Input] click trk#12 @cx,cy backend=<name> result=<ok|fail>`.
2. The router logs **which backend actually landed** the click (not just that it tried the first).
3. **Default-backend safety:** a default should work with zero external helper processes running. If the selected virtual backend's bridge is down, that must be a **visible warning**, not a quiet fall-through — the earlier log's `"Controller bridge is not running; sending real key fallback"` should surface in the UI, not just the trace.

*(Backend-internal/virtual-device implementation is out of scope for this plan; this phase is purely about logging and deterministic selection so you can see what happened.)*

---

## Appendix — one-shot verify script
Save as `tools/verify_debugtrace.sh` (or run inline) after each build:
```bash
#!/usr/bin/env bash
L="${1:-DebugTrace.log}"
echo "A minScore unique:"; grep -oE 'minScore=[0-9.]+' "$L" | sort -u
echo "B mode/clientCoords:"; grep -oE 'mode=[a-z]+' "$L"|sort|uniq -c; grep -oE 'clientCoords=(True|False)' "$L"|sort|uniq -c
echo "C gate:"; grep -oE 'raw=[0-9]+ entities=[0-9]+' "$L" \
 | awk -F'[= ]' '{if($2>$4)g++; if($2<$4)b++; t++} END{printf "frames=%d raw>ent=%d raw<ent=%d => %s\n",t,g,b,(b?"FAIL":(g?"OK":"SUSPECT"))}'
echo "F track lock (top ids):"; grep -oE 'trk#[0-9]+' "$L" | sort | uniq -c | sort -rn | head
```
