# Ragnarok Roboflow Dataset Notes

Checked July 3, 2026.

## What The Public Projects Do

- Most projects are object-detection datasets made from real Ragnarok screenshots.
- Labels are drawn around visible sprites or UI objects, not OCR text.
- Small focused datasets can score well when they target one map or a small monster set.
- Good public examples include:
  - TenQue `ragnarok-bot-okavr`: 177 images shown on the project page, model dataset listed as 423 images, classes for player/other player/portal plus Anaconda, Boa, Poporing, Wolf, reported mAP@50 93.5%.
  - Ragnarok Test `ragnarok-payon-cave-tc7rb`: 134 images, classes Familiar, monsters, Poporing, Portal, Skeleton, Zombie, YOLOv8s model, reported mAP@50 92.4%.
  - PDI `ragnarok-online-enemy-detector`: 489 images, classes bat, poporing, skeleton, zombie.
  - Frapercan `ragnarok-online`: 173 images, classes dragon fly and poison spore.
  - X III `ragnarok-monsters`: 67 images, 10 monster/player classes.
  - creamy `ragnarok`: 96 images, classes player, snake, wolf, poporing, spore.
  - Ragnarok `ragnarok-monster-detection`: 25 images, Dead Willow, Elder Willow, items.

## What To Copy

- Use real screenshots as the base detector data.
- Include player/portal/loot/UI target classes so the app can ignore them instead of confusing them with monsters.
- Keep broad `monster` detection separate from exact monster identity.
- Train exact monster identity from GRF sprite frames and icon embeddings, because Roboflow projects with exact monster classes are small and map-specific.
- Generate synthetic RO scenes from GRF sprites to cover facing direction, animation frames, partial overlap, scale, and movement.

## What Not To Copy Blindly

- Do not train only one tiny map/class set and expect it to generalize.
- Do not keep item classes in the monster identity model unless the feature specifically needs loot identification.
- Do not rely on a single frame per monster; RO sprites change heavily by direction and animation.

## 4ViviTools Approach

The current pipeline matches the useful pattern:

1. Merge paid Roboflow screenshots into `tools/ocr-train/yolo_real`.
2. Normalize classes into the app-facing detector set:
   `monster`, `player`, `loot`, `portal`, `target`, `target_hp`, `player_hp`.
3. Generate GRF synthetic monster scenes and mix them into `yolo_real`.
4. Train YOLO to find entities quickly.
5. Use the monster/skill icon embedder trained from GRF frames to identify the exact monster name.

This gives broad detection from real screenshots plus exact monster naming from sprite knowledge.

## Gameplay Video Ingest

Videos can improve YOLO when they are converted into labeled frames. Do not feed raw video directly
to training; YOLO needs images plus bounding-box labels.

Supported local workflow:

1. Use a gameplay recording you own or have permission to use.
2. Run `tools/ocr-train/INGEST_VIDEO.bat`.
3. Paste the local video path.
4. The script samples non-duplicate frames into `tools/ocr-train/video_frames/<video>/frames`.
5. It pseudo-labels high-confidence boxes with the current YOLO model and stages the dataset under
   `tools/ocr-train/TrainingData/Video_<video>.yolov8`.
6. Check `tools/ocr-train/video_frames/<video>/review_pseudo_labels.jpg`.
7. Run `RUN_EVERYTHING_2060S.bat`; `merge_datasets.py` will include the staged video dataset.

Use high confidence pseudo-labels as seeds, not as unquestioned truth. If the review sheet shows bad
boxes, delete that `TrainingData/Video_*.yolov8` folder or lower the frame count and try again.
