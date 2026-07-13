# 4ViviTools — OCR & Bot Roadmap

Living memory of the OCR/bot overhaul. Each task has its own markdown in `tasks/`. Statuses reflect the code as of this session.

**Summary:** 17 done · 3 in progress · 20 pending.

## Priority order (paid guide §8)

1. Better capture → 2. Better preprocessing → 3. Region pipelines → 4. Knowledge engine → 5. Temporal → 6. OCR settings → 7. Recognition width → 8. Multi-pass → 9. RO fine-tune → 10. YOLO.

We are doing the cheap high-impact items first (per the guide); the model/DXGI/super-res/architecture are the heavy tail.

## Needs your attention (decisions / your GPU / packages)

- **#52 / #66 run the RO fine-tune** — your GPU; biggest accuracy gain. Steps in task-52.md.
- **#38 DXGI capture** — add Vortice.Windows package; untestable here.
- **#48 Super-resolution** — ship an SR ONNX model; heavier.
- **#54 ONNX DirectML/CUDA** — add the DirectML package.
- **#69 Production architecture** — large; usually last.

## All tasks

| Task | Status | Title |
|---|---|---|
| #17 | ✅ done | [Move nav bar to top](tasks/task-17.md) |
| #18 | ✅ done | [Merge OCR+Bot into one tab](tasks/task-18.md) |
| #33 | ⬜ pending | [Key grid + OCR-filled skill table](tasks/task-33.md) |
| #34 | ⬜ pending | [Wire smart bot to OCR (skills/pots/ammo)](tasks/task-34.md) |
| #35 | 🟡 in progress | [Persist bot + OCR config](tasks/task-35.md) |
| #36 | ⬜ pending | [Dedup controls across merged Bot sections](tasks/task-36.md) |
| #37 | ⬜ pending | [Fit merged Bot UI to 1920x1080](tasks/task-37.md) |
| #38 | ⬜ pending | [DXGI Desktop Duplication capture](tasks/task-38.md) |
| #39 | ⬜ pending | [Lanczos upscale](tasks/task-39.md) |
| #40 | ✅ done | [Denoise (Median) mode](tasks/task-40.md) |
| #41 | ✅ done | [Morphological Close mode](tasks/task-41.md) |
| #42 | ✅ done | [Multi-pass OCR](tasks/task-42.md) |
| #43 | ✅ done | [Per-field confidence thresholds](tasks/task-43.md) |
| #44 | ✅ done | [Confidence smoothing](tasks/task-44.md) |
| #45 | ✅ done | [Levenshtein dictionary correction](tasks/task-45.md) |
| #46 | ✅ done | [Per-field engine (Windows OCR for digits)](tasks/task-46.md) |
| #47 | ✅ done | [Ensemble OCR majority vote](tasks/task-47.md) |
| #48 | ⬜ pending | [Super-resolution (ESPCN/RealESRGAN)](tasks/task-48.md) |
| #49 | ⬜ pending | [YOLO buff/status icon detection](tasks/task-49.md) |
| #50 | ⬜ pending | [Template matching (skill bar/buttons)](tasks/task-50.md) |
| #51 | ✅ done | [Cast-bar pixel reading](tasks/task-51.md) |
| #52 | 🟡 in progress | [Run synthetic RO-font rec fine-tune](tasks/task-52.md) |
| #53 | ⬜ pending | [PP-OCRv5 Server rec model](tasks/task-53.md) |
| #54 | ⬜ pending | [ONNX Runtime DirectML/CUDA](tasks/task-54.md) |
| #55 | ✅ done | [Overlay perf: decouple OCR fps + cache](tasks/task-55.md) |
| #56 | ⬜ pending | [Named zone presets](tasks/task-56.md) |
| #57 | ⬜ pending | [LAB CLAHE](tasks/task-57.md) |
| #58 | ✅ done | [ROI/per-mark OCR](tasks/task-58.md) |
| #59 | ✅ done | [CLAHE + adaptive threshold](tasks/task-59.md) |
| #60 | ✅ done | [RO thresholds + angle-off + rec-only](tasks/task-60.md) |
| #61 | ✅ done | [Temporal + zoned + quiet border](tasks/task-61.md) |
| #62 | ✅ done | [HP/SP bars + ground-truth Verify](tasks/task-62.md) |
| #63 | ⬜ pending | [Guide §1 System Architecture](tasks/task-63.md) |
| #64 | ⬜ pending | [Guide §2 Image Processing/SR](tasks/task-64.md) |
| #65 | ⬜ pending | [Guide §3 PP-OCR/RapidOcrNet/ONNX](tasks/task-65.md) |
| #66 | 🟡 in progress | [Guide §4 Custom RO model](tasks/task-66.md) |
| #67 | ⬜ pending | [Guide §5 CV instead of OCR](tasks/task-67.md) |
| #68 | ⬜ pending | [Guide §6 Knowledge+Temporal+Multi](tasks/task-68.md) |
| #69 | ⬜ pending | [Guide §7 Production Architecture](tasks/task-69.md) |
| #70 | ⬜ pending | [Guide §8 Exact recommendations](tasks/task-70.md) |

## How to resume

1. `dotnet build 4rVivi.sln -c Release` — confirm green.
2. Pick the lowest pending task in the priority order.
3. Open its `tasks/task-NN.md`, do the 'Next' steps, update status here.
