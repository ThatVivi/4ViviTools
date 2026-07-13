# Task #47 — Ensemble OCR majority vote

**Status:** ✅ done

## What it is
Paddle + Windows (+Tesseract) majority vote on hard fields.

## Done so far
Ensemble engine: ReadRectFrom 'Ensemble' runs Paddle + Windows OCR, agrees/boosts or picks higher confidence. 'Ensemble (vote)' toggle.

## Next / how to continue
Run all engines on the crop, vote; needs #46 first.

## Files
OcrService.cs
