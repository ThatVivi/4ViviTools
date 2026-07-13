#!/usr/bin/env python3
"""Add known false-positive gameplay frames as YOLO negative examples.

A YOLO negative example is an image with an empty label file. Put screenshots
where boxes appeared on ground/effects/UI into tools/ocr-train/false_positive_frames
and run this script before the next training run.
"""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path


def main() -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--false-positives", default=str(here / "false_positive_frames"))
    parser.add_argument("--images-out", default=str(here / "yolo_real" / "images" / "train"))
    parser.add_argument("--labels-out", default=str(here / "yolo_real" / "labels" / "train"))
    parser.add_argument("--max-ratio", type=float, default=0.20,
                        help="Cap negatives to this fraction of current train images.")
    args = parser.parse_args()

    fp_dir = Path(args.false_positives)
    images_out = Path(args.images_out)
    labels_out = Path(args.labels_out)
    images_out.mkdir(parents=True, exist_ok=True)
    labels_out.mkdir(parents=True, exist_ok=True)

    images = sorted(
        [*fp_dir.glob("*.png"), *fp_dir.glob("*.jpg"), *fp_dir.glob("*.jpeg"), *fp_dir.glob("*.webp")]
    )
    if not images:
        print(f"No false-positive frames found in {fp_dir}")
        return 0

    existing = sorted(
        [*images_out.glob("*.png"), *images_out.glob("*.jpg"), *images_out.glob("*.jpeg"), *images_out.glob("*.webp")]
    )
    cap = max(1, int(len(existing) * max(0.0, args.max_ratio)))
    if len(images) > cap:
        print(f"Capping hard negatives {len(images)} -> {cap} ({args.max_ratio:.0%} of current train images)")
        images = images[:cap]

    for image in images:
        dst_name = f"neg_{image.stem}{image.suffix.lower()}"
        shutil.copy2(image, images_out / dst_name)
        (labels_out / f"neg_{image.stem}.txt").write_text("", encoding="utf-8")
        print(f"negative added {dst_name}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
