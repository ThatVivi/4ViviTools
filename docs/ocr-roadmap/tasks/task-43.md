# Task #43 — Per-field confidence thresholds

**Status:** ✅ done

## What it is
Each field its own MinScore floor.

## Done so far
Per-category confidence floors auto-set in AddMark (Monster/Item/Skill/Map/Char names get their own floor; numerics use global). Per-mark MinScore applied in read gate.

## Next / how to continue
Add a per-row Min column to the readout table to set it.

## Files
OcrMark.cs; OcrReaderViewModel.cs
