# 4ViviTools — MASTER Overnight Execution Plan (autonomous)
**Date:** 2026-07-11 · **Mode:** non-stop, self-verifying, no approval gates · **Executor:** Codex
**Covers:** the full arc of this session — OCR/vision wiring, phantom/duplicate-box fixes, the Vision Assist GRF, the model retrain, and the residual task board.

> **Honest boundary.** Codex can do all *code, tests, synthetic verification, and training* autonomously overnight. It **cannot** load a GRF into the live RO client and confirm red boxes render, nor read the game screen — those are **operator gates** for Vivi in the morning (Part F). Everything else runs unattended.

---

## A. Agent Operating Contract (read once, obey all night)

1. **Non-stop loop.** For each task: implement → `dotnet build` → `dotnet test` → run the task's **acceptance check** → if green, `git commit` with the task id → next task. No pausing for approval.
2. **Evidence before "done."** A task is complete only when its acceptance command prints the pass condition. Paste the command output into the commit body. Never claim success from "build passed" alone.
3. **Self-correct.** On failure: read the error, fix, re-run the same gate, max 3 attempts, then leave the task `BLOCKED:` in `docs/superpowers/OVERNIGHT-LOG.md` with the exact error and move on — do not stall the whole run on one item.
4. **Commit discipline.** One commit per task, message `T<id>: <what> — <acceptance line>`. Keep `main` buildable at every commit.
5. **Scope guard.** Do **not** touch or "improve" the virtual-input backends (VIIPER/FakerInput/ViGEm/reWASD). Input work is limited to logging/observability only. Everything else (vision, OCR, model, GRF, UI, trackers) is in scope.
6. **Morning report.** Append a summary to `docs/superpowers/OVERNIGHT-LOG.md`: tasks done/blocked, acceptance outputs, artifacts produced, and the exact Part F checklist for Vivi.
7. **Do the research first** where a task is tagged `[RESEARCH]` (Part E) — validate binary formats / training params online before writing code, and cite the source in the commit.

Reference specs already in-repo (this plan is the hub; details live there):
- `2026-07-11-vision-wiring-and-detector-fix-plan.md` (T1–T6)
- `2026-07-11-phantom-and-duplicate-boxes-fix.md` (F1–F6)
- `2026-07-11-vision-grf-implementation-plan.md` + `-next-steps-and-fixes.md` (S1–S6, B1–B4)
- `2026-07-11-next-test-verification-plan.md` (verification battery)

---

## B. PHASE 1 — Vision Assist GRF: finish + self-test (P0, autonomous)

### B1 — Observability + unnamed-marker fallback *(the agreed next change)*
**File:** `src/4rVivi.App/Services/VisionAssistMarkerDetector.cs`, `src/4rVivi.App/Services/OcrService.cs`
**Do:** count `rawBoxes` (all shape-passing rectangles) separately from `decoded` (code matched < 1.45). A box that fails decode is emitted as `MobId=-1, Name="Monster", Score=0.3f`, **attackable**. Log the truth:
```csharp
// Detect(): rawBoxes++ for every rectangle passing LooksLikeRectangleBorder
// dec==null -> markers.Add(new VisionAssistMarker(c.X,c.Y,c.W,c.H,-1,"Monster",0.3f));
// OcrService.AddVisionAssistFinds:
DebugTrace.Write("OCR", $"visionAssist=True targetSource={(markers.Count>0?"grf":"yolo")} " +
  $"boxDet={rawBoxes} codeReads={decoded} nameUnknown={rawBoxes-decoded} manifest='{VisionAssistManifestPath}'.");
```
**Acceptance (synthetic — Codex can run without the game):** add a test that renders a baked frame via the generator's bake, feeds it to `VisionAssistMarkerDetector.Detect`, and asserts `boxDet==1` and (for a manifest mob) `codeReads==1`; and for a red box with a garbage code asserts `boxDet==1, codeReads==0, name=="Monster"`.
```bash
dotnet test .../4rVivi.Core.Tests.csproj --filter VisionAssist -c Release --nologo   # green
```

### B2 — Bot targets GRF markers without a tracker
**File:** `src/4rVivi.Core/Automation/SmartBotEngine.cs`, marker→`SceneEntity` mapping.
**Do:** GRF-sourced entities are `Attackable=true` on arrival (optional "seen twice" 1-frame guard; no ByteTrackLite). Ensure `PerMonsterSkillKey` still keys off `Name` (works for named; `"Monster"` uses the default attack).
**Acceptance (unit):** a test feeding one GRF `SceneEntity` to the bot's target selector returns that entity as the chosen target.

### B3 — Round-trip identity test (the real contract) `[RESEARCH]`
**File:** new `tests/4rVivi.Core.Tests/VisionGrfRoundTripTests.cs` + a headless bake harness.
**Do:** for N sample mob ids: `color_code(id)` → paint cells → `NormalizedRgbDistance` decode → assert id recovered, **and** repeat with each cell multiplied by 0.6/0.75/0.9 (simulated cave lighting) → still recovered. This proves the brightness-invariance claim mechanically, before the live test.
**Acceptance:** all ids recovered at every brightness factor; commit the pass table.

### B4 — Generator hardening
**File:** `tools/vision-grf/build_vision_grf.py`
**Do:** (a) validate `CODE_CELL`/`BOX_PX` equal the detector constants at import (assert + print), (b) write `VisionAssist.manifest.json` with `codeCell`, `boxPx`, `boxColor` so the detector self-configures instead of hardcoding, (c) confirm output magic is `Master of Magic` (`assert head==b'Master of Magic'`), (d) emit `sharedSprites` audit already present — additionally fail loudly if >20% of scoped mobs are unmapped.
**Acceptance:**
```bash
python -m py_compile tools/vision-grf/build_vision_grf.py
python tools/vision-grf/build_vision_grf.py --selftest   # add a --selftest that bakes a synthetic sprite, repacks, reopens, verifies magic + one decode
```

---

## C. PHASE 2 — Model retrain (P0, autonomous, the literal all-nighter) `[RESEARCH]`

This is what "overnight" is for — training runs for hours unattended.

### C1 — Assemble the dataset
- `tools/ocr-train/label_real.py` → pre-label whatever real frames exist in `real_frames/`; if none exist, **generate them from the sample logs' `rawSample` boxes** is not possible (no images) → instead Codex documents in the OVERNIGHT-LOG that Vivi must drop real frames, and proceeds with synthetic + hard-negatives it *can* do.
- `tools/ocr-train/mine_hard_negatives.py` → fold any `false_positive_frames/` into `yolo_real` as empty-label backgrounds, capped at 10–20% of the set.

### C2 — `[RESEARCH]` before training, confirm current best-practice params
Research and cite (Ultralytics docs + PP-OCR docs), then set:
- YOLO11n negative-sample ratio, `imgsz`, epochs, `mosaic`/`close_mosaic`, and whether to freeze backbone for a small real set.
- Confirm `rect`/`cache` settings for an 8GB card (RTX 2060S).
Write chosen params + citations into the commit.

### C3 — Train + export + calibrate (ship-gate)
```bash
cd tools/ocr-train
python train_yolo.py --data yolo_real/data.yaml --model yolo11n.pt --epochs 60 --imgsz 640 --batch 16
python export_onnx.py --weights yolo_real/runs/entity/weights/best.pt --out ../../src/RapidOcrNet/models/yolo/entity.onnx
python check_calibration.py > ../../docs/superpowers/calibration-$(date +%H%M).txt
```
**Ship-gate (wired into the export/bat "refuse bad detector" floor):** only replace `entity.onnx` if calibration shows ≥80% of true-positive detections ≥0.55 and a clear sub-0.30 junk mass. Otherwise keep the old model and log `BLOCKED: calibration failed`.
**Acceptance:** `calibration-*.txt` printed; new `entity.onnx` only if gate passes; both logged.

---

## D. PHASE 3 — Residual task board (P1–P2, autonomous)

Pull from `docs/ocr-roadmap` and the earlier plans. Do in this order, each with build+test+commit:

- **#43 UI column** for per-field MinScore (mechanism exists). `[App]`
- **#33 Skill-Spammer key grid → bot skill instructions** (per-key). `[App/Core]`
- **#34 wire smart bot skills/pots/ammo to OCR roles.** `[Core]`
- **#35 persist bot+OCR config** to the active profile (finish). `[Core/App]`
- **#36 dedup merged Bot controls / #37 fit 1920×1080.** `[App axaml]`
- **#50 consume `TemplateMatchService`** for fixed UI elements. `[OcrServer/App]`
- **#56 apply `RegionProfiles` thresholds** per-region in the live read path. `[App/OcrServer]`
- **UI sweep:** numeric boxes 4-digit + expandable; remaining free-text → `SearchPicker`; click-box overlay toggle. `[App axaml]`
Each: acceptance = build+test green and a one-line behavioral note in the commit. Anything needing a running client → defer to Part F, don't block.

---

## E. `[RESEARCH]` directives (do these online, cite in commits)

1. **RO SPR v2.1 truecolor + ACT offset format** — validate `SprWriter` byte layout (header `SP`, version 0x201, indexed=0 + rgba frames). Cross-check against GRFEditor-exported truecolor SPR. Sources: rAthena wiki / GRFEditor threads.
2. **GRF 0x200 container + custom magic** — confirm header (46-byte: signature[15], key[15], tableOffset, seed, fileCount, version), zlib file table, and that stock clients require `Master of Magic` (custom magic needs a Nemo patch). Apply to the reader loosening + writer.
3. **RO map ambient lighting** — how sprites are color-multiplied by map light; confirms the hue/ratio decode approach and informs B3 cell size. 
4. **Ultralytics YOLO11 — negatives & calibration** — background/negative image ratio, and reading confidence separation (not just mAP).
5. **PP-OCRv5 rec fine-tune** — current recommended lr/batch/sampler for a mixed synthetic+real set; confirm GTC-removal still advised.
6. **ByteTrack params** — `track_buffer`/`min_hits` norms (informs the OFF-GRF fallback tracker, not GRF mode).
For each: 1–3 line finding + URL in the commit body; if a finding contradicts current code, open a fix task.

---

## F. Operator gates for Vivi (morning — only these need the game)

Codex queues these in `OVERNIGHT-LOG.md`; Vivi runs them with the client:
1. `python tools/vision-grf/build_vision_grf.py --client "<root>" --scope map` → add `0=VisionAssist.grf` to DATA.INI.
2. Confirm red boxes render in-game (bright map, then dark cave).
3. Enable `Vision Assist GRF`, farm ~90s in the cave, grab `DebugTrace.log` + a screenshot.
4. Run:
```bash
bash tools/verify_debugtrace.sh DebugTrace.log
grep -oE 'boxDet=[0-9]+ codeReads=[0-9]+ nameUnknown=[0-9]+' DebugTrace.log | sort | uniq -c
```
5. Confirm Smart Bot attacks the marker entities.
6. Report back log + screenshot. `boxDet` high + `codeReads` low ⇒ trigger B3 (8px cells + median). `boxDet` low ⇒ box render/threshold. Bot idle ⇒ B2.

---

## G. Master acceptance battery (run after every phase)
```bash
dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release --nologo   # 0 warn/err
dotnet test  "...4rVivi.Core.Tests.csproj" -c Release --nologo                     # all green
bash tools/verify_debugtrace.sh DebugTrace.log                                     # when a log exists
python -m py_compile tools/vision-grf/build_vision_grf.py tools/ocr-train/*.py
```

## H. Priority / order for the night
1. **B1 → B3 → B2 → B4** (GRF path: observability + prove identity mechanically + bot targeting + generator selftest).
2. **C1–C3** model retrain (kick early; it runs for hours in parallel with D).
3. **E research** interleaved (before the format/training tasks it gates).
4. **D** task board, cheapest first (#43, #33, #34, #35, #36/#37, #50, #56, UI sweep).
5. Write **OVERNIGHT-LOG.md** + the **Part F** checklist for morning.

**End state by morning:** GRF path fully unit-proven (identity survives simulated cave lighting), a freshly calibrated `entity.onnx` (or an explicit BLOCKED with reason), several task-board items shipped, a research log with citations, and a crisp in-client checklist waiting for Vivi.

---

## I. PHASE 4 — "Best version" upgrades (P1, autonomous where possible)

High-value features that move the tool from "works" to "best-in-class". Each is a normal task (build+test+commit); ones needing the live client defer their final check to Part F.

- **I1 — Vision Assist GRF Phase 2 (baked readable name strip).** Expand each frame canvas upward by `H_name`, draw the real name (bold, white-on-dark), and **recompute that frame's `.act` Y-offset** so the body still lands. Needs the ACT writer. Ships human-visible real names above monsters. `[Core/Grf, tools/vision-grf]`
- **I2 — Per-map auto-profile.** When OCR `MapName` changes, auto-load a per-map profile: skills, roam box, thresholds, target list (from `map_mobs.json`). Farming a new map "just works". `[Core/Servers, App]`
- **I3 — Per-track name voting finished (T5).** Icon vote keyed by `TrackId`; map-focus biases the name only when the icon score is low; never creates/deletes a box. (OFF-GRF path.) `[Core/Ocr]`
- **I4 — Loot-assist via GRF.** Optionally bake a distinct-color box on item-drop sprites too, so loot pickup uses the same color-scan path (separate color from monster red). `[tools/vision-grf, App]`
- **I5 — Self-calibrating thresholds.** A 30-second "learn" run that samples true/false detection scores and auto-sets `TrackConf/AttackConf` and the marker reject gate from the data — no more magic numbers. `[App/Core]`
- **I6 — Worker resilience.** Auto-restart the OCR/CUDA worker on crash; surface a single visible status; never silently fall to Windows OCR without a toast. `[App/Services]`
- **I7 — Session analytics.** EXP/hr, Zeny/hr, kills, drops, uptime, detector health — a live artifact/HUD the user can keep open. `[App]`
- **I8 — Multi-client vision (pillar #8).** One shared detector, round-robin windows, hot focus + cold others; per-window profile binding UI. `[App/Core]`

## J. PHASE 5 — Deep-research tasks (P1, `[RESEARCH]` — big, do the reading first)

These require real online research; each produces a short findings doc under `docs/research/` with citations, then (if warranted) a fix task.

- **J1 — RO rendering & map-lighting model.** Exactly how the client multiplies sprite pixels by map ambient + fog. Goal: guarantee the GRF color-code decode across every map, not just caves. Output: the exact tint model + the decode normalization that survives it. `docs/research/ro-lighting.md`
- **J2 — Native C# SPR/ACT/GRF writer (drop GRFEditor dependency).** Full spec of SPR v2.1 truecolor, ACT frame/anchor structure, GRF 0x200 table. Goal: `SprWriter`/`ActWriter`/`GrfWriter` with byte-exact output validated against GRFEditor. `docs/research/grf-spr-act-format.md`
- **J3 — Authoritative `mobId → sprite` map from client Lua.** Parse `datainfo\npcidentity.lua` + `jobname.lua` (+ `data\luafiles514\...`) to build `mobid_sprite_map.json` automatically instead of a hand table. This is the #1 wrong-name risk killer. Ship a generator `tools/vision-grf/build_sprite_map.py`. `docs/research/mobid-sprite-map.md`
- **J4 — Best OCR for RO fonts.** Compare PP-OCRv5 mobile vs server, TrOCR, and pure template/7-seg reading for the numeric HUD. Recommend the accuracy/speed winner per field. `docs/research/ocr-approach.md`
- **J5 — Detector head comparison.** YOLO11n vs RT-DETR vs a pure red-box CV (GRF mode) for RO sprites; when each wins. `docs/research/detector-comparison.md`
- **J6 — Anti-flicker / tracking literature.** ByteTrack/OC-SORT params for sparse, teleporting targets; confirm the OFF-GRF fallback config. `docs/research/tracking.md`

## K. PHASE 6 — Cleanup / dead-code purge (P1, autonomous, careful)

**Goal:** remove what was abandoned so the repo is lean and Codex never re-derives dead paths.
**Method (evidence-based, no blind deletes):**
1. Build with `-warnaserror:CS0169,CS0414,CS8321` off but capture unused-symbol warnings; run a reference scan:
```bash
# unreferenced .cs types (rough): every public type name not grep-found outside its own file
for t in $(grep -rhoE 'class [A-Z][A-Za-z0-9]+' src --include=*.cs | awk '{print $2}' | sort -u); do
  n=$(grep -rl "\b$t\b" src --include=*.cs | grep -v "/$t\.cs" | wc -l); [ "$n" -eq 0 ] && echo "UNREFERENCED: $t"; done
```
2. Candidates to review for removal (confirm unreferenced before deleting): superseded training scripts (`run.py` vs `run_patched.py`, `train_all.py` vs `train_export.py`), monitor-only capture code paths replaced by client capture, stale model backups (`*.bak`, `*.prebak` in `models/icons`, `models/v5`), duplicate/experimental services, and any input backend files that are **not** referenced by the shipping input path (leave anything referenced; do not add new ones).
3. Produce `docs/CLEANUP-REPORT.md` listing each removed file + why + the grep proving it was unreferenced. Delete only items with zero references. Keep `main` green.
**Acceptance:** build+test still green after purge; `CLEANUP-REPORT.md` committed; repo file count drops with zero behavior change.

## L. PHASE 7 — Orientation map + user guide (P0 final, do LAST every night)

- **L1 — `docs/CODEX-MAP.md`** — the "when we get lost" reference. Keep it **current**: paths, the vision path, OCR path, Smart Bot path, skills/attack path, delays, confidences, thresholds, model locations, engine list, roles. A seed version ships with this plan; Codex must update it whenever a path/const changes. This file is the first thing to read at the start of any future session.
- **L2 — `docs/USER_GUIDE.md`** — newbie-friendly. Update it at the end of the run to match what shipped: what to download, exact steps, every checkbox, troubleshooting. Seed version ships with this plan.

**Rule:** L1 and L2 are updated at the **end of every working block** so they never drift from the code. A commit that changes a path/const without updating `CODEX-MAP.md` is incomplete.

## M. Night ordering (updated)
`B1→B3→B2→B4` (GRF proof) → kick **C** training (runs for hours) → interleave **J** research before its dependent tasks → **I** best-version upgrades → **D** task board → **K** cleanup → **L** update CODEX-MAP + USER_GUIDE → write `OVERNIGHT-LOG.md` + Part F checklist. Never stop on a single blocked item; log it and continue until usage ends.
