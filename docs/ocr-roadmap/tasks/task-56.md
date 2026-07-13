# Task #56 — Named zone presets

**Status:** 🟡 in progress

## What it is
Predefined RO zones (HP/SP/Map/Monster/Chat...) each own settings. Guide §1 ROI.

## Done so far
- Live Auto marks now use `RegionProfiles` for role-specific preprocessing and scale.
- Existing saved name-like marks (`Monster`, `TargetName`, `MapName`, `SkillName`, etc.) are corrected to text mode at runtime, so they are not routed through numeric OCR.
- Low-confidence/failed OCR crops are saved to `tools/ocr-train/hard_examples/<Role>/` with JSON metadata for retraining.

## Next / how to continue
Add preset mark sets and make detector thresholds/profile routing external in `Data/RegionProfiles.json`.

## Files
OcrReaderViewModel; new presets data
