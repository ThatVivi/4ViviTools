# Section 8 — Exact Recommendations + Priority (Status vs Code)

Guide §8 (ocr_guide.txt lines 3919–end). Verbatim priorities/numbers + current code state.
Legend: **[DONE]** · **[PARTIAL]** · **[TODO]**.

## Priority Order (exact, 1–10)
1. Better Capture
2. Better Preprocessing
3. Region-Specific Pipelines
4. Knowledge Engine
5. Temporal Engine
6. OCR Settings
7. Recognition Width
8. Multi-Pass OCR
9. RO Fine-Tuning
10. YOLO

## Biggest Accuracy Gains Ranked (exact, 1–10)
1. Knowledge Engine
2. RO Recognition Training
3. Region-Specific Pipelines
4. Temporal Voting
5. Recognition Width 640/960
6. ESRGAN
7. Multi-Pass OCR
8. Windows OCR Consensus
9. Detector Tuning
10. YOLO

## Phases (exact)
- **Phase 1:** Knowledge Engine · Temporal Voting · Region Profiles
- **Phase 2:** Recognition Width · OpenCV Pipelines · Multi-Pass OCR
- **Phase 3:** Windows OCR Consensus · ESRGAN
- **Phase 4:** RO Fine-Tuned Recognition Model
- **Phase 5:** YOLO · Template Matching · State Engine

## Exact numeric recommendations → code state

| # | Guide recommendation (exact) | Code value | Status | Evidence |
|---|---|---|---|---|
| Detector — RO profile | det_db_thresh **0.15**, det_db_box_thresh **0.20**, unclip **2.5**, use_dilation true, max_candidates 5000 | `OcrTuningConfig`: DetBoxThresh 0.15, DetBoxScoreThresh 0.20, DetUnclipRatio 2.50 | **[PARTIAL/DONE]** | `AppSettings.cs` OcrTuningConfig matches the three thresholds exactly. **use_dilation / max_candidates not exposed** (no `Dilation`/`max_candidates` field). |
| Detector — aggressive (monster/chat) | thresh **0.10**, box **0.15**, unclip **3.0** | RegionProfiles MonsterName/TargetName/Chat: DetThresh 0.10, DetBoxThresh 0.15, Unclip 3.00 | **[PARTIAL]** | Exact values present in `RegionProfiles.cs` — but that class is **orphaned (unwired)**; the live `OcrTuningConfig` is single global, no aggressive-profile switch per region. |
| Recognition width | 3x48x**320 → 640**, test **960** | CRNN height 48 fixed; width = **dynamic tight-fit** (`dstWidth = src.W * 48/src.H`), no 320/640/960 cap | **[PARTIAL]** | `TextRecognizer.cs:16` `CrnnDstHeight=48`; the `320` constant is commented out (`:17`). Width is per-crop proportional, not padded to a fixed 640/960. Detector-side `LimitSideLen`/`MaxSideLen` = **960** (`OcrTuningConfig`), so 960 is applied at the *detector*, not the recognizer width. The guide's literal "set RecImgShape 320→640→960" is **not** done. |
| drop_score | 0.5 → **0.3** | TextScore 0.20 (`OcrTuningConfig`); RapidOcrOptions.Default TextScore 0.5f | **[DONE/exceeds]** | Live tuning `TextScore=0.20` (`AppSettings.cs`), pushed to worker via `ApplyTuning()` `textScore=...`. Below the recommended 0.3 (more permissive). Engine default record still 0.5 but overridden at runtime. |
| use_angle_cls | true → **false** | DoAngle **false** (`OcrTuningConfig`) | **[DONE]** | `AppSettings.cs`: `DoAngle=false` ("angle classifier off… avoids 6/9 flips"); sent as `doAngle=0`. (RapidOcrOptions.Default still DoAngle=true but runtime config wins.) |
| ORT SessionOptions | GraphOptimization **ORT_ENABLE_ALL**, ExecutionMode **ORT_PARALLEL**, EnableMemoryPattern, EnableCpuMemArena, IntraOp 8 / InterOp 4 | `GetDefaultSessionOptions`: GraphOptimizationLevel **ORT_ENABLE_ALL**, Inter=Intra=numThread | **[PARTIAL]** | `RapidOcr.cs:440` sets `ORT_ENABLE_ALL` (matches). **Missing:** ExecutionMode=ORT_PARALLEL, EnableMemoryPattern, EnableCpuMemArena not set. Threads: both Inter & Intra = single `numThread` (CpuThreads=8), not the 8/4 split. |
| Execution provider | DirectML (AMD/Intel) / CUDA (NVIDIA) / TensorRT; 2–20x | none | **[TODO]** | `grep` finds no `AppendExecutionProvider`/DirectML/CUDA in `RapidOcr.cs`. **CPU-only.** |
| Multi-Pass OCR | A/B/C/D (upscale, +CLAHE, +threshold, +sharpen), keep best | `MultiPass` toggle → `ReadRectBest` | **[PARTIAL]** | `OcrService.cs:564 ReadRectBest`, toggle in `OcrTogglesConfig.MultiPass` + ViewModel `:655/:665`. Multiple preprocessings tried, highest score kept. Exact A/B/C/D recipe set not codified. |
| Windows OCR consensus | PP-OCR + Windows OCR → consensus (not PP-OCR only) | `Ensemble` toggle | **[PARTIAL]** | `WindowsOcrEngine` present; `OcrService.cs:546` Ensemble path votes Paddle + Windows. Toggle `OcrTogglesConfig.Ensemble`. Consensus is a simple per-read pick, not weighted. |
| Temporal voting | Last **20** frames | TemporalVotingService Window **20** | **[DONE]** | `TemporalVotingService.cs` `Window=20`, applied `OcrReaderViewModel.cs:672`. String-majority (not weighted by confidence). |
| Knowledge Engine | mob_db / item_db / skill_db / map_index | `OcrNameCorrector` + GameDatabase | **[DONE]** | `OcrReaderViewModel.cs:235–239` loads Class/Monster/Map/Item/Skill dictionaries; `Correct()` applied `:671`. |
| OpenCV pipelines (Lanczos 3x, LAB, CLAHE 4.0, Close 2x2 / Adaptive) | per-region recipes | RegionProfiles defines them | **[PARTIAL]** | Exact recipes encoded in `RegionProfiles.cs` (Scale 3, Clahe 4.0, Lab, Close, AdaptiveThreshold) but **unwired**. Live path uses per-mark `Preprocess` string + `Upscale`/`Sharpen` in `OcrService`. |
| ESRGAN | RealESRGAN **x2plus**, only Map/Target/Chat/Inventory (not full screen); +10–30% | none | **[TODO]** | No ESRGAN/RealESRGAN anywhere in code. |
| YOLO | **YOLO11n**, replace buff/skill/icon/equip/status detection (not OCR replacement) | YOLOv8 single-class `entity.onnx` | **[PARTIAL]** | `EntityDetector.cs:13` runs **YOLOv8**, not YOLO11n; detects entity sprites (one class) + icon embedder naming. Not used for buff/skill/equip/status icon classes. |
| RegionProfiles.json file | external override | supported | **[DONE]** | `RegionProfiles.cs:66–67` loads `Data/RegionProfiles.json` override (class itself still unwired into OCR pipeline). |

## "Files I would modify first" (guide Stage 15) → status
| Guide item | Status |
|---|---|
| `KnowledgeService.cs` | **[DONE]** as `OcrNameCorrector.cs` (different name, same role) |
| `TemporalVotingService.cs` | **[DONE]** exists & wired |
| `RegionProfiles.json` | **[PARTIAL]** loader exists, profiles unwired into OCR read path |
| RapidOCR `RecImgShape` 320→640/960 | **[TODO]** width still dynamic tight-fit; only detector LimitSide=960 |
| `use_angle_cls` disable | **[DONE]** DoAngle=false |
| `drop_score` 0.5→0.3 | **[DONE]** TextScore=0.20 |

## Tally
- **[DONE]:** 7 (drop_score, angle-cls off, det RO thresholds, temporal-20, knowledge engine, GraphOpt ENABLE_ALL, json loader)
- **[PARTIAL]:** 8 (rec width, ORT options, aggressive profile, multi-pass, ensemble, OpenCV pipelines, YOLO v8-vs-11n, region profiles unwired)
- **[TODO]:** 4 (DirectML/CUDA provider, ESRGAN x2plus, use_dilation/max_candidates, RO-trained rec model — Phase 4)
