#!/usr/bin/env python3
"""Score-distribution check for the entity detector on real validation frames.

This does not replace mAP. It answers the runtime question: are detector scores
separated enough that TrackConf=0.30 and AttackConf=0.55 are sane?
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from ultralytics import YOLO


def main() -> int:
    here = Path(__file__).resolve().parent
    repo = here.parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default=str(repo / "src" / "RapidOcrNet" / "models" / "yolo" / "entity.onnx"))
    parser.add_argument("--images", default=str(here / "yolo_real" / "images" / "val"))
    parser.add_argument("--conf", type=float, default=0.05)
    parser.add_argument("--imgsz", type=int, default=640)
    parser.add_argument("--track-conf", type=float, default=0.30)
    parser.add_argument("--attack-conf", type=float, default=0.55)
    args = parser.parse_args()

    images_dir = Path(args.images)
    images = sorted(
        [*images_dir.glob("*.png"), *images_dir.glob("*.jpg"), *images_dir.glob("*.jpeg"), *images_dir.glob("*.webp")]
    )
    if not images:
        print(f"No validation images found in {images_dir}")
        return 2

    model = YOLO(args.model, task="detect")
    scores: list[float] = []
    per_image: list[tuple[str, int, float]] = []
    for image in images:
        result = model.predict(str(image), conf=args.conf, imgsz=args.imgsz, verbose=False)[0]
        image_scores = [float(box.conf) for box in result.boxes]
        scores.extend(image_scores)
        per_image.append((image.name, len(image_scores), max(image_scores) if image_scores else 0.0))

    if not scores:
        print("No detections at calibration conf floor.")
        return 3

    arr = np.array(scores, dtype=np.float32)
    percentiles = np.percentile(arr, [5, 10, 25, 50, 75, 90, 95]).round(3)
    print(f"images={len(images)} detections={len(scores)}")
    print(f"p05/p10/p25/p50/p75/p90/p95={percentiles.tolist()}")
    print(f"frac>=track({args.track_conf:.2f})={(arr >= args.track_conf).mean():.3f}")
    print(f"frac>=attack({args.attack_conf:.2f})={(arr >= args.attack_conf).mean():.3f}")
    print(f"frac<track({args.track_conf:.2f})={(arr < args.track_conf).mean():.3f}")

    worst = sorted(per_image, key=lambda row: row[2])[:10]
    print("lowest-max-score images:")
    for name, count, max_score in worst:
        print(f"  {name}: detections={count} max={max_score:.3f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
