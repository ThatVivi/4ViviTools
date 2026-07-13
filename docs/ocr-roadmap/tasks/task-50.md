# Task #50 — Template matching (skill bar/buttons)

**Status:** ✅ done

## What it is
MatchTemplate for fixed UI elements.

## Done so far
- `TemplateMatchService` implements normalized cross-correlation.
- `OcrService.ScanIcons` calls `RefineIconCells`, which builds per-cell templates and uses `FindBest` to refine skill/buff icon cell positions before icon recognition.

## Next / how to continue
Live client verification: confirm skill/buff icon cells stay aligned when the bar is slightly shifted or scaled.

## Files
OcrServer / OcrService
