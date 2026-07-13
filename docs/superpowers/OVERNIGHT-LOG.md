# 4ViviTools Overnight Log

Date: 2026-07-11

## Research Notes

- RO SPR: RagnarokFileFormats documents RGB frames as RGBA data and notes transparency support for version 2.0+, which matches the Phase-1 truecolor SPR writer approach. Source: https://github.com/Duckwhale/RagnarokFileFormats/blob/master/SPR.MD
- RO SPR v2.1 layout cross-check: z0q's Ragnarok SPR format notes describe little-endian SPR files and version 0x201, supporting the current `SP`, minor 1, major 2 header convention. Source: https://z0q.neocities.org/ragnarok-online-formats/spr/
- GRF: z0q's GRF notes describe the `Master of Magic` signature; `grf-loader` describes GRF as compressed Ragnarok asset archives. This supports writing standard magic while keeping the reader permissive for custom examples. Sources: https://z0q.neocities.org/ragnarok-online-formats/grf/ and https://github.com/vthibault/grf-loader/blob/master/README.md
- YOLO negatives: Ultralytics recommends background images with no labels to reduce false positives, around 0-10% background images. Source: https://docs.ultralytics.com/yolov5/tutorials/tips-for-best-training-results
- ByteTrack: the original paper associates low-confidence detections with tracklets to recover true objects while filtering background detections; this matches the non-GRF fallback architecture. Source: https://arxiv.org/abs/2110.06864

## Completed

### B1 - Vision Assist GRF observability and unnamed-marker fallback

- `VisionAssistMarkerDetector` now returns `VisionAssistDetectionResult` with `RawBoxes`, `Decoded`, and `NameUnknown`.
- Red boxes whose code cells cannot be decoded are emitted as `MobId=-1`, `Name="Monster"`, `Score=0.30f` instead of disappearing.
- `OcrService` logs real GRF marker counters: `boxDet`, `codeReads`, and `nameUnknown`.

Acceptance:

```text
dotnet test "...4rVivi.Core.Tests.csproj" -c Release --no-restore --filter VisionAssist --nologo
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4
```

### B2 - Smart Bot can target GRF markers immediately

- GRF-sourced scan finds carry `Source="grf"`.
- OCR-to-scene mapping marks GRF entities as pre-confirmed.
- `ByteTrackLite` honors pre-confirmed detections by starting them at `LiveScene.TrackMinHits`, making them attackable on the first frame while YOLO detections still use normal confirmation.

Acceptance:

```text
VisionAssistMarkerDetectorTests.Preconfirmed_marker_track_is_attackable_on_first_frame passed.
```

### B3 - Marker identity survives simulated cave lighting

- Added a synthetic round-trip test for several mob ids at 0.60, 0.75, and 0.90 brightness.
- All tested ids decoded correctly at every brightness factor.

Acceptance:

```text
VisionAssistMarkerDetectorTests.Marker_identity_survives_simulated_cave_lighting passed.
```

### B4 - Generator hardening

- `VisionAssist.manifest.json` now includes `codeCell`, `codeCellPx`, `boxPx`, and `boxColor`.
- Detector reads `codeCellPx` and `boxPx` from the manifest.
- Generator asserts output GRF magic is `Master of Magic`.
- Generator refuses output when more than 20% of scoped mobs are skipped.
- Added `--selftest` for synthetic SPR bake, GRF pack, standard magic check, and GRF read-back.

Acceptance:

```text
python -m py_compile tools\vision-grf\build_vision_grf.py
python tools\vision-grf\build_vision_grf.py --selftest
[grf] selftest constants boxPx=2 codeCell=5 codeCells=3
[grf] selftest ok
```

### B5 - GRF UX and docs sync

- Added an OCR Reader `Vision Assist GRF` card with `Use Vision Assist GRF` and manifest path controls.
- Current packaged-operator flow builds outside the app with `tools\ocr-train\Grf\BUILD_VISION_GRF_TO_OUTPUT.bat`; the app runtime consumes the already-built `VisionAssistLibrary.grf`/`VisionAssist.manifest.json` artifacts instead of building them. `VisionGrfPicker` promotes the monsters that should actually render markers.
- Removed the duplicate Advanced-only Vision Assist controls; the feature is now visible in the normal OCR path.
- Synced `docs\CODEX-MAP.md` and `docs\USER_GUIDE.md` with the current architecture: manifest-driven marker constants, GRF-sourced preconfirmed SceneItems, and hotbar-card Smart Bot setup.
- Aligned persisted default monster confidence with `VisionConfig.DefaultTrackConfidence` (`0.50`) instead of the old `0.30`.
- External format check supported Phase 1: SPR is the image/frame container, ACT owns animation/frame placement, and GRF file tables live after the 46-byte header. So the safest shipped path is still unchanged frame sizes + unchanged ACT + baked pixels, with readable name-strip/ACT-offset work deferred to Phase 2.

References checked:

- https://github.com/Duckwhale/RagnarokFileFormats/blob/master/SPR.MD
- https://z0q.neocities.org/ragnarok-online-formats/spr/
- https://z0q.neocities.org/ragnarok-online-formats/act/
- https://z0q.neocities.org/ragnarok-online-formats/grf/

Verification:

```text
dotnet build 4rVivi.sln -c Release --no-restore --nologo
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --no-restore --no-build --filter VisionAssist --nologo
Passed! Failed: 0, Passed: 4, Skipped: 0, Total: 4

Added `LiveScene_returns_preconfirmed_grf_marker_as_attackable_target`; reran focused suite:

```text
dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --no-restore --filter VisionAssist --nologo
Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

dotnet test tests\4rVivi.Core.Tests\4rVivi.Core.Tests.csproj -c Release --no-restore --no-build --nologo
Passed! Failed: 0, Passed: 53, Skipped: 0, Total: 53

dotnet publish src\4rVivi.App\4rVivi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --nologo
Published to src\4rVivi.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

## In Progress

### C - Overnight YOLO retrain

Started `tools\ocr-train\RUN_OVERNIGHT_YOLO_2060S.bat`.

Observed progress:

```text
Preflight OK.
map_mobs refreshed from local rAthena checkout: 432 maps, 3190 map-monster rows.
No optional real_frames found for label_real.py.
No optional false_positive_frames found for mine_hard_negatives.py.
Merged YOLO train=3792 val=411, hard-negative images=9.
Video pseudo-label dataset skipped until manually approved.
Synthetic GRF monster scene generation started: 9000 scenes, imgsz=640.
```

Correction after log audit:

- Vivi did not run training overnight.
- The current session's later BAT run did not complete; the terminal handle disappeared and the log shows the latest run stopped during `10_yolo_synth` after `scene 3500/9000`.
- `.full_training_state.json` contains YOLO checkpoint `09_yolo_merge` only for the latest run, so stages `10_yolo_synth` through `13_build` are incomplete for that run.
- There is an older successful YOLO export from `2026-07-11 05:10:44` already installed at `src\RapidOcrNet\models\yolo\entity.onnx`; that older run reported `mAP50=0.906`, `mAP50-95=0.550`, monster `mAP50=0.956`.
- `gen_yolo_scenes.py` now supports `--resume`, skips bad individual scenes with traceback logging, and fails only if fewer than 92% of requested scenes are generated.
- `train_everything_2060s.py` now calls the synthetic scene generator with `--resume` so the next run continues filling missing scene IDs instead of throwing away partial work.

Safe resume command:

```bat
cd /d "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train"
python train_everything_2060s.py --skip-text --skip-icons --fresh-yolo-train --yolo-epochs 100 --yolo-scenes 9000 --imgsz 640 --min-yolo-map50 0.70 --min-yolo-map5095 0.35
```

If using `RUN_OVERNIGHT_YOLO_2060S.bat`, it still resets YOLO checkpoints by design and redoes merge; use the command above for checkpoint resume.

### C2 - Vision GRF builder hardening after agent audit

- Added `tools\vision-grf\build_sprite_map.py` to build `mobid_sprite_map.json` from client `npcidentity.lua/lub` and `jobname.lua/lub`.
- `build_vision_grf.py` now auto-builds `mobid_sprite_map.json` if it is missing.
- GRF filename table encoding now tries `cp949`/`euc-kr` before `cp1252`, so Korean internal paths such as `data\sprite\몬스터\...` are not replaced with `???`.
- `DATA.INI` source GRF discovery is supported; generated `VisionAssist.grf` is skipped as a source when rebuilding.
- `build_sprite_map.py` rejects empty/invalid parsed Lua/LUB data instead of silently writing a bad map.

Developer-only manual GRF builder flow. Vivi/operator packaged builds should use `tools\ocr-train\Grf\BUILD_VISION_GRF_TO_OUTPUT.bat`, which writes `tools\ocr-train\Grf\output\VisionAssistLibrary.grf` and `tools\ocr-train\Grf\output\VisionAssist.manifest.json`.

```powershell
cd "D:\vs code clone 4rtool\4ViviTools"
$ro = "C:\Path\To\Your\Ragnarok"
py -3 tools\vision-grf\build_vision_grf.py --selftest
py -3 tools\vision-grf\build_sprite_map.py --client $ro --out tools\vision-grf\mobid_sprite_map.json
python tools\vision-grf\build_vision_grf.py --client $ro --scope map --out "$ro\VisionAssist.grf" --manifest "$ro\VisionAssist.manifest.json"
```

Then put `0=VisionAssist.grf` first in `DATA.INI`, restart the RO client, and enable `Use Vision Assist GRF` in OCR Reader.

Verification:

```text
python -m py_compile tools\vision-grf\build_vision_grf.py tools\vision-grf\build_sprite_map.py tools\ocr-train\gen_yolo_scenes.py tools\ocr-train\train_everything_2060s.py
python tools\vision-grf\build_vision_grf.py --selftest
[grf] selftest ok
dotnet build 4rVivi.sln -c Release --no-restore --nologo
Build succeeded. 0 Warning(s), 0 Error(s)
```

### D/#50 - TemplateMatchService consumer

- Roadmap drift found: `TemplateMatchService` is already consumed by `OcrService.ScanIcons` through `RefineIconCells`.
- Updated `docs/ocr-roadmap/tasks/task-50.md` to mark it done and point to the actual consumer.

### D/#56 - RegionProfiles runtime routing

- Verified `OcrService` owns `RegionProfiles`, sends `BuildRegionConfig` for profiled anchor text reads, and `OcrReaderViewModel` applies `SuggestPreprocess` / `SuggestScale` for live marks.
- Remaining #56 scope is preset mark-set UX, not the runtime threshold/profile routing requested by the master plan.

### D/#33 and D/#34 - Smart Bot key grid and OCR/action wiring

- Roadmap drift found: the Smart Bot key-card hotbar grid, skill/buff/pot/ammo/bag action cards, searchable pickers, per-key delays, and automatic controller assignments already exist.
- Verified engine sync paths in `SmartBotViewModel`: skill rows feed per-monster skill keys, SP requirements, and skill delay dictionaries; buff rows feed refresh intervals; HP/SP/Ygg rows feed Autopot; ammo/ammo-bag rows feed SmartBot ammo state.
- Updated `docs/ocr-roadmap/tasks/task-33.md` and `task-34.md` with current status and live-client verification notes.

### D/#35 - Persist bot and OCR config

- Verified OCR toggles, marks, tuning, Vision Assist options, and Smart Bot profile state are persisted.
- Verified `ProfileConfig.SmartBot` and `SmartBotViewModel.SaveBotProfile()` cover hotbar cards, monster rules, walk box, controller mapping, input method, autopot bridge, ammo/bag settings, reconnect keys, hotkeys, timing overrides, and target map.
- Updated `docs/ocr-roadmap/tasks/task-35.md`.

## Morning Operator Gates

1. Run `tools\ocr-train\Grf\BUILD_VISION_GRF_TO_OUTPUT.bat`.
2. Confirm it wrote `tools\ocr-train\Grf\output\VisionAssistLibrary.grf` and `tools\ocr-train\Grf\output\VisionAssist.manifest.json`.
3. Open `tools\VisionGrfPicker\publish\VisionGrfPicker.exe`, load `VisionAssistLibrary.grf`, pick target monsters, and press Apply.
4. Copy the picker-edited `VisionAssistLibrary.grf` into the RO client folder, then add `0=VisionAssistLibrary.grf` as the first DATA.INI entry.
5. Confirm red boxes render in-game on a bright map, then in the dark cave.
6. In 4ViviTools, set the Vision Assist manifest path to `tools\ocr-train\Grf\output\VisionAssist.manifest.json`, enable `Vision Assist GRF`, and farm for about 90 seconds.
7. Upload `DebugTrace.log` and a screenshot with boxes visible.
8. Run:

```bash
bash tools/verify_debugtrace.sh DebugTrace.log
grep -oE 'boxDet=[0-9]+ codeReads=[0-9]+ nameUnknown=[0-9]+' DebugTrace.log | sort | uniq -c
```

If `boxDet` is high but `codeReads` is low, enlarge marker cells to 8 px and use median sampling. If `boxDet` is low, inspect GRF loading and red threshold. If the bot is idle, inspect GRF source propagation and attackability logs.
