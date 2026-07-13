# Session progress — autonomous OCR batch

Build was green before this batch. All changes below are **static-verified (brace/XML)** but
**not compiled** — run `dotnet build 4rVivi.sln -c Release` first; if anything fails it's isolated to
the files listed per task.

## Done this session (build to activate)
- Fixed 4 SkiaSharp `SKFilterQuality` warnings → `SKSamplingOptions(SKCubicResampler.Mitchell)`.
- **#40 Median** + **#41 Close** preprocess modes (denoise + reconnect strokes).
- **#42 Multi-pass** read (best-of: field mode + CLAHE + Adaptive + high-contrast + sharpened).
- **#44 Confidence smoothing** (Stable: conf>=0.92 publishes instantly).
- **#45 Levenshtein dictionary** correction confirmed already wired (class/monster/map/item/skill).
- **#46 Windows-OCR-per-field** ("Windows OCR for numbers" toggle + per-mark Engine override).
- **#51 Cast-bar** role (pixel %).
- **#43 per-field MinScore** mechanism wired (UI column still TODO).
- New preprocess modes total: 18 color layers + Adaptive + CLAHE + Median + Close.

## Earlier this session (also needs the build)
- Rec-only path for marks (RapidOcr.RecognizeLine + worker REC + client + service) — big fix.
- RO detector thresholds lowered; ORT graph-opt → ALL.
- Synthetic fine-tune renderer (synth.py/patterns.py) made RO-realistic.

## Needs YOU (kept for later — see tasks/)
- **#52 run the RO fine-tune** (your GPU) — biggest accuracy gain. Steps in tasks/task-52.md.
- **#38 DXGI**, **#48 super-res**, **#54 DirectML** — need NuGet packages, untestable here.
- **#69 production architecture** — large, last.

## Resume
See README.md table. Next cheap items: **#55 perf skip-unchanged**, **#56 named zones**,
**#39 Lanczos**, **#57 LAB-CLAHE**, **#47 ensemble** (needs #46), then the heavy tail.
