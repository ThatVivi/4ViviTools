# Task #55 — Overlay perf: decouple OCR fps + cache

**Status:** ✅ done

## What it is
OCR ~10fps, cache, only-OCR-on-change. Guide overlay opt.

## Done so far
Skip-unchanged: per-mark gray signature (CropGray) cached; if a region's pixels didn't change, republish the last value and skip OCR. 'Skip unchanged' toggle (default on).

## Next / how to continue
Add per-region pixel-change detection (CropGray diff) to skip unchanged reads.

## Files
OcrReaderViewModel.BgTick; OcrService.CropGray
