# Section 1 — System Architecture (Status vs Code)

Guide §1 (ocr_guide.txt lines 2–334). Status mapped against actual `src/`.
Legend: **[DONE]** in place · **[PARTIAL]** exists but incomplete/unwired · **[TODO]** absent.

## Target pipeline: Capture → Frame → ROI → Image/Vision → OCR/Object → Knowledge → Temporal → Overlay

| Layer | Guide recommendation | Status | Evidence |
|---|---|---|---|
| **1. Capture** | DXGI Desktop Duplication; avoid `Graphics.CopyFromScreen`/BitBlt/GDI (blur, DPI, color artifacts); 1:1 GPU framebuffer | **[TODO]** | `OcrService.cs:152,214,498,512` all use `Graphics.FromImage(...).CopyFromScreen` — the exact GDI path the guide forbids. `CaptureWindow` (`OcrService.cs:484`) uses `PrintWindow` then falls back to CopyFromScreen. No DXGI anywhere. |
| **2. Frame Manager** | Never OCR every frame; decouple game/OCR/overlay FPS; cache static values (name, level, class); `FrameCache{ Cur, Prev, FrameId, Timestamp }` | **[PARTIAL]** | `SkipUnchanged` toggle (`AppSettings.cs` OcrTogglesConfig, default true) skips unchanged regions; ViewModel reads one frame then per-mark from it. No `FrameCache` class, no Prev/FrameId/Timestamp, no explicit OCR-Hz decoupling. |
| **3. ROI Manager** | Dedicated per-region OCR (Char/HP-SP/Map/Target/Inventory/Chat/Monster/Party); `RoiRegion{ Name, Bounds, Profile }`; dynamic anchor-based ROI for resolution independence | **[PARTIAL]** | User-defined regions exist as `OcrMark` / `OcrRegion` (`OcrService.cs:14`, `AppSettings.OcrMarks`). Named, bounded, per-mark. But coords are static/manual — **no anchor/template-derived dynamic ROI**, not resolution-independent. |
| **4. OCR Profiles** | Per-region preprocessing (Map/Chat/Inventory/Monster/Stats), each w/ Upscale/CLAHE/Threshold/Denoise | **[PARTIAL]** | `RegionProfiles.cs` defines exactly these profiles with guide-exact values (Scale/Clahe/Threshold/AdaptiveThreshold/Lab/Close + det thresholds). **BUT orphaned** — `grep` finds zero references outside its own file; OcrService uses per-mark `Preprocess`/`Sharpen` strings instead. Not wired into the pipeline. |
| **5. OCR Routing** | Route per region: HP/SP→Windows OCR, Map/Inv/Monster→PP-OCRv5, Chat→Tesseract; `IOcrEngine.Read(Mat)` | **[PARTIAL]** | All three engines present: `RapidOcrClient` (`_rapid`), `WindowsOcrEngine` (`_winOcr`, `OcrService.cs:30`), Tesseract (`using Tesseract`). Routing per-mark via `EngineFor()` (`OcrReaderViewModel.cs:150`): Paddle / Windows / Ensemble. No formal `IOcrEngine` interface; no Tesseract-for-chat default. |
| **6. Knowledge Engine** | Don't trust OCR text; snap to valid game strings (Payon vs Poyon) from mob_db/item_db/skill_db/map_index/job_db | **[DONE]** | `OcrNameCorrector.cs` — per-role Levenshtein dictionary correction. Wired: `OcrReaderViewModel.cs:235–239` loads ClassName/Monster/MapName/ItemName/SkillName from `GameDatabase`; applied at `:671` (`_corrector.Correct`). |
| **7. Temporal Engine** | Store last ~10 frames; weighted vote (Confidence + Occurrence + Dictionary) to kill ~80% false positives | **[PARTIAL]** | `TemporalVotingService.cs` — last-N (default 20) majority vote per role; applied at `OcrReaderViewModel.cs:672`. Votes on the **string only**; the guide's *weighted* formula (confidence + dictionary score blended) is not implemented — plain majority/recency vote. |
| **8. Overlay Engine** | Never show raw OCR; OCR→Validation→Dictionary→Temporal→Overlay; keep last valid value; never display `?` | **[PARTIAL]** | Order honored: read → `Correct` (dictionary) → `Vote` (temporal) before display (`OcrReaderViewModel.cs:665–672`). Empty reads don't pollute vote buffer. No explicit "hold last valid value on failure / never render `?`" cache in the read loop — relies on SkipUnchanged + vote standing-winner. |

## Vision-vs-OCR split (guide: HP/SP bars, buffs, icons, cast/target should NOT be OCR'd)

| Item | Status | Evidence |
|---|---|---|
| Entity/sprite detection via YOLO (not OCR) | **[PARTIAL]** | `EntityDetector.cs` runs a YOLOv8 single-class ONNX (`entity.onnx`) in OcrServer. Detects *where* entities are; icon naming via `IconRecognizer` (MobileNetV3 embedder, cosine match). |
| Buff / skill / status / HP-SP bar by pixel/template (not OCR) | **[TODO]** | No template-matching or pixel-reading layer for HP/SP bars, buffs, cast bar, target marker. HP/SP still read as OCR text (numeric marks). |

## Tally
- **[DONE]:** 1 (Knowledge Engine; string-temporal also functional)
- **[PARTIAL]:** 6 (Frame, ROI, Profiles, Routing, Temporal-weighting, Overlay-cache, Vision-split)
- **[TODO]:** 2 (DXGI Capture, pixel/template HUD layer)
