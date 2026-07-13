#!/usr/bin/env python3
"""
yolo_qc_supervision.py -- quick QA for YOLO datasets before overnight training.

Uses Roboflow Supervision when available to render annotated sample sheets and reports
class counts, empty labels, missing pairs, and tiny boxes. It is intentionally read-only.
"""
from __future__ import annotations

import argparse
import json
import math
from collections import Counter
from pathlib import Path

import cv2
import numpy as np
import yaml

try:
    import supervision as sv
except Exception as exc:  # pragma: no cover - CLI dependency check
    raise SystemExit("Install requirements first: pip install -r requirements.txt\n" + str(exc))

HERE = Path(__file__).resolve().parent
DEFAULT_DATA = HERE / "yolo_real" / "data.yaml"


def resolve_split(root: Path, value: str) -> Path:
    p = Path(value)
    return p if p.is_absolute() else root / p


def read_label(label_path: Path, width: int, height: int) -> tuple[np.ndarray, np.ndarray]:
    boxes: list[list[float]] = []
    classes: list[int] = []
    if not label_path.is_file():
        return np.zeros((0, 4), dtype=float), np.zeros((0,), dtype=int)
    for line in label_path.read_text(encoding="utf-8", errors="replace").splitlines():
        parts = line.split()
        if len(parts) < 5:
            continue
        cls, cx, cy, bw, bh = int(float(parts[0])), *map(float, parts[1:5])
        x0 = (cx - bw / 2.0) * width
        y0 = (cy - bh / 2.0) * height
        x1 = (cx + bw / 2.0) * width
        y1 = (cy + bh / 2.0) * height
        boxes.append([x0, y0, x1, y1])
        classes.append(cls)
    return np.asarray(boxes, dtype=float), np.asarray(classes, dtype=int)


def annotate_sheet(images: list[Path], label_dir: Path, names: list[str], out_path: Path, max_images: int) -> None:
    tiles: list[np.ndarray] = []
    box_annotator = sv.BoxAnnotator(thickness=2)
    label_annotator = sv.LabelAnnotator(text_scale=0.45, text_thickness=1)
    for img_path in images[:max_images]:
        img = cv2.imread(str(img_path))
        if img is None:
            continue
        h, w = img.shape[:2]
        boxes, classes = read_label(label_dir / f"{img_path.stem}.txt", w, h)
        dets = sv.Detections(xyxy=boxes, class_id=classes)
        labels = [names[c] if 0 <= c < len(names) else str(c) for c in classes]
        img = box_annotator.annotate(img, dets)
        img = label_annotator.annotate(img, dets, labels=labels)
        scale = min(240 / max(1, w), 160 / max(1, h))
        tile = cv2.resize(img, (max(1, int(w * scale)), max(1, int(h * scale))))
        canvas = np.full((160, 240, 3), 18, dtype=np.uint8)
        y = (160 - tile.shape[0]) // 2
        x = (240 - tile.shape[1]) // 2
        canvas[y:y + tile.shape[0], x:x + tile.shape[1]] = tile
        tiles.append(canvas)
    if not tiles:
        return
    cols = 4
    rows = math.ceil(len(tiles) / cols)
    sheet = np.full((rows * 160, cols * 240, 3), 12, dtype=np.uint8)
    for idx, tile in enumerate(tiles):
        y = (idx // cols) * 160
        x = (idx % cols) * 240
        sheet[y:y + 160, x:x + 240] = tile
    out_path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(out_path), sheet)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=str(DEFAULT_DATA))
    ap.add_argument("--out", default=str(HERE / "yolo_real" / "qc_supervision"))
    ap.add_argument("--max-images", type=int, default=48)
    ap.add_argument("--tiny-px", type=int, default=6)
    args = ap.parse_args()

    data_path = Path(args.data).resolve()
    root = data_path.parent
    spec = yaml.safe_load(data_path.read_text(encoding="utf-8")) or {}
    names_raw = spec.get("names") or []
    names = list(names_raw.values()) if isinstance(names_raw, dict) else list(names_raw)
    out = Path(args.out)
    report: dict[str, object] = {"data": str(data_path), "classes": names, "splits": {}}

    for split in ("train", "val"):
        img_dir = resolve_split(root, str(spec.get(split, f"{split}/images")))
        label_dir = img_dir.parent.parent / "labels" / img_dir.name if img_dir.name in ("images", "train", "val") else img_dir.parent / "labels"
        if img_dir.name == "images":
            label_dir = img_dir.parent / "labels"
        images = sorted([p for p in img_dir.glob("*") if p.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"}])
        counts: Counter[str] = Counter()
        empty = missing = tiny = boxes_total = 0
        sampled: list[Path] = []
        for img_path in images:
            img = cv2.imread(str(img_path))
            if img is None:
                continue
            h, w = img.shape[:2]
            lab = label_dir / f"{img_path.stem}.txt"
            if not lab.is_file():
                missing += 1
                continue
            boxes, classes = read_label(lab, w, h)
            if len(classes) == 0:
                empty += 1
                continue
            boxes_total += len(classes)
            for box, cls in zip(boxes, classes):
                counts[names[cls] if 0 <= cls < len(names) else str(cls)] += 1
                if (box[2] - box[0]) < args.tiny_px or (box[3] - box[1]) < args.tiny_px:
                    tiny += 1
            sampled.append(img_path)
        annotate_sheet(sampled, label_dir, names, out / f"{split}_sample.jpg", args.max_images)
        report["splits"][split] = {
            "images": len(images),
            "boxes": boxes_total,
            "empty_labels": empty,
            "missing_labels": missing,
            "tiny_boxes": tiny,
            "class_counts": dict(counts),
            "sample_sheet": str(out / f"{split}_sample.jpg"),
        }

    out.mkdir(parents=True, exist_ok=True)
    (out / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
