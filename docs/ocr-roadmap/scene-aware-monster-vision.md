# Scene-Aware Monster Vision

Updated: 2026-07-10

## What changed

- `LiveScene` now keeps short-lived entity tracks instead of replacing the whole monster list every frame.
- When one monster is missed for a frame, the tracker holds the last confirmed/global-shifted box instead of projecting it forward. That keeps boxes pinned instead of swimming around the monster.
- Monitor-capture entity updates keep track identity between frames instead of clearing the tracker every tick.
- The overlay runs at an 8 ms frame push target, which is roughly 120 FPS when the machine and UI thread can keep up.
- Smart Bot map focus is generated from rAthena spawn files into `src/4rVivi.Core/Data/map_mobs.json`. When a farm map is selected, impossible out-of-map sprite names are downgraded to generic `Monster` instead of being shown as a wrong name.
- Floating monster-name OCR is conservative. Numeric or garbled text such as `1000` no longer overwrites a good sprite/YOLO monster detection.
- Bad monster-name reads are saved under `tools/ocr-train/hard_examples/MonsterName/` with a PNG crop and JSON metadata.
- `gen_yolo_scenes.py` now uses all sprite frames, mirrored directions, scale/brightness/blur variation, fixed HUD negatives, occlusion, and map-scroll style backgrounds.

## Why this matters for Ragnarok Online

RO is not a static screenshot problem. When the player moves, the map and monsters shift together while the HUD stays fixed. Monster sprites also face multiple directions and animate through many frames. The detector must learn all of that:

- train on many frames per monster, not only `0.png`;
- ignore fixed UI/HUD regions;
- treat missed frames as temporary uncertainty, not proof that the monster vanished;
- never trust numeric floating OCR as a monster name unless it is validated against a monster dictionary.

## Regenerate synthetic detector scenes

Use the one-button runner for the full detector refresh:

```powershell
& "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\RUN_OVERNIGHT_YOLO_2060S.bat"
```

That runner installs dependencies, verifies Supervision/ByteTrack support packages, rebuilds Roboflow + video + synthetic data, creates QC sheets, fresh-trains YOLO, exports `entity.onnx`, and builds the Release app.

Manual scene regeneration is still available:

```powershell
python "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\gen_yolo_scenes.py" --force --scenes 9000 --imgsz 640 --max-objs 16
```

The main detector still trains from `tools/ocr-train/yolo_real/data.yaml`, which is built from the paid datasets. Synthetic scenes are a supplement for direction/frame/background coverage.

## Retrain detector

```powershell
python "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\merge_datasets.py"
python "D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\train_yolo.py" --fresh --epochs 100 --imgsz 640 --min-map50 0.70 --min-map5095 0.35
```

After training, the exported model is copied to:

```text
D:\vs code clone 4rtool\4ViviTools\src\RapidOcrNet\models\yolo\entity.onnx
```

## Hard examples

When the app sees a monster-like box but floating text looks numeric or unusable, it writes:

```text
D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\hard_examples\MonsterName
```

Review those crops after real play sessions. They are the best source for the next improvement pass because they show exactly what the live OCR failed to understand.
