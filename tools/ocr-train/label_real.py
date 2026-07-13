#!/usr/bin/env python3
"""Pre-label real Ragnarok gameplay frames with the current entity detector.

Drop real screenshots into tools/ocr-train/real_frames, run this script, then
open the generated labels in your labeler and correct them. The goal is fast
human correction, not blind acceptance of the current model.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from ultralytics import YOLO


def main() -> int:
    here = Path(__file__).resolve().parent
    repo = here.parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default=str(repo / "src" / "RapidOcrNet" / "models" / "yolo" / "entity.onnx"))
    parser.add_argument("--frames", default=str(here / "real_frames"))
    parser.add_argument("--out", default=str(here / "yolo_real" / "labels_real"))
    parser.add_argument("--conf", type=float, default=0.20)
    parser.add_argument("--iou", type=float, default=0.45)
    parser.add_argument("--imgsz", type=int, default=640)
    args = parser.parse_args()

    frames = Path(args.frames)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    model = YOLO(args.model, task="detect")

    images = sorted(
        [*frames.glob("*.png"), *frames.glob("*.jpg"), *frames.glob("*.jpeg"), *frames.glob("*.webp")]
    )
    if not images:
        print(f"No images found in {frames}")
        return 0

    for image in images:
        result = model.predict(str(image), conf=args.conf, iou=args.iou, imgsz=args.imgsz, verbose=False)[0]
        lines: list[str] = []
        for box in result.boxes:
            cls = int(box.cls)
            xywhn = box.xywhn[0].tolist()
            lines.append(f"{cls} " + " ".join(f"{v:.6f}" for v in xywhn))
        (out / f"{image.stem}.txt").write_text("\n".join(lines), encoding="utf-8")
        print(f"pre-labeled {image.name}: {len(lines)} boxes")

    print(f"Labels written to {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
