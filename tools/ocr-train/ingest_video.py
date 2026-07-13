#!/usr/bin/env python3
"""
ingest_video.py -- turn RO gameplay videos into YOLO training frames.

Video is useful for detection only after it becomes frames with boxes. This script samples diverse
frames, optionally pseudo-labels them with the current YOLO model, and writes a Roboflow/YOLO-style
dataset under TrainingData so merge_datasets.py can include it on the next run.

Use videos you recorded yourself or have permission to use.

Examples:
  python ingest_video.py --src "D:/RO/video.mp4" --sample-every 1.0 --max-frames 900
  python ingest_video.py --src "D:/RO/video.mp4" --pseudo-label --stage-trainingdata
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import re
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
VIDEO_ROOT = HERE / "video_frames"
TRAINING_DATA = HERE / "TrainingData"
DEFAULT_MODEL = HERE / "yolo_real" / "runs" / "entity" / "weights" / "best.pt"
CLASSES = ["monster", "player", "loot", "portal", "target", "target_hp", "player_hp"]


def log(msg: str) -> None:
    print(msg, flush=True)


def pip_install(*pkgs: str) -> None:
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", *pkgs])


def ensure_cv2():
    try:
        import cv2  # noqa
        return cv2
    except Exception:
        log("[video] installing opencv-python for video decoding...")
        pip_install("opencv-python")
        import cv2
        return cv2


def ensure_ultralytics():
    try:
        from ultralytics import YOLO  # noqa
        return YOLO
    except Exception:
        log("[video] installing ultralytics for pseudo-labeling...")
        pip_install("ultralytics")
        from ultralytics import YOLO
        return YOLO


def slugify(path: Path) -> str:
    s = re.sub(r"[^a-zA-Z0-9]+", "_", path.stem).strip("_").lower()
    return s[:48] or "video"


def ahash(im: Image.Image, size: int = 8) -> int:
    g = im.convert("L").resize((size, size), Image.Resampling.BILINEAR)
    vals = list(g.getdata())
    avg = sum(vals) / len(vals)
    bits = 0
    for i, v in enumerate(vals):
        if v >= avg:
            bits |= 1 << i
    return bits


def hamming(a: int, b: int) -> int:
    return (a ^ b).bit_count()


@dataclass
class SampleStats:
    fps: float
    total_frames: int
    kept: int
    skipped_near_duplicate: int


def extract_frames(src: Path, out_dir: Path, sample_every: float, max_frames: int, hash_distance: int, jpg_quality: int) -> SampleStats:
    cv2 = ensure_cv2()
    cap = cv2.VideoCapture(str(src))
    if not cap.isOpened():
        raise RuntimeError(f"Could not open video: {src}")

    fps = float(cap.get(cv2.CAP_PROP_FPS) or 30.0)
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    step = max(1, int(round(fps * max(0.05, sample_every))))
    out_dir.mkdir(parents=True, exist_ok=True)

    kept = 0
    skipped_dup = 0
    hashes: list[int] = []
    frame_index = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        if frame_index % step != 0:
            frame_index += 1
            continue

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        im = Image.fromarray(rgb)
        hv = ahash(im)
        if any(hamming(hv, old) <= hash_distance for old in hashes[-40:]):
            skipped_dup += 1
            frame_index += 1
            continue
        hashes.append(hv)

        stem = f"v_{kept:06d}_f{frame_index:08d}"
        im.save(out_dir / f"{stem}.jpg", quality=jpg_quality)
        kept += 1
        if kept % 100 == 0:
            log(f"[video] extracted {kept} frames...")
        if max_frames > 0 and kept >= max_frames:
            break
        frame_index += 1

    cap.release()
    return SampleStats(fps=fps, total_frames=total, kept=kept, skipped_near_duplicate=skipped_dup)


def yolo_label_line(cls: int, xyxy, width: int, height: int) -> str | None:
    x0, y0, x1, y1 = [float(v) for v in xyxy]
    x0 = max(0.0, min(width - 1.0, x0))
    y0 = max(0.0, min(height - 1.0, y0))
    x1 = max(0.0, min(width - 1.0, x1))
    y1 = max(0.0, min(height - 1.0, y1))
    bw = max(0.0, x1 - x0)
    bh = max(0.0, y1 - y0)
    if bw < 4 or bh < 4:
        return None
    cx = (x0 + x1) / 2.0 / width
    cy = (y0 + y1) / 2.0 / height
    return f"{cls} {cx:.6f} {cy:.6f} {bw / width:.6f} {bh / height:.6f}"


def pseudo_label(frames_dir: Path, dataset_dir: Path, model_path: Path, conf: float, iou: float, val_frac: float, seed: int) -> tuple[int, int, int]:
    if not model_path.is_file():
        raise FileNotFoundError(f"YOLO model not found: {model_path}")
    YOLO = ensure_ultralytics()
    model = YOLO(str(model_path))
    imgs = sorted(frames_dir.glob("*.jpg"))
    if not imgs:
        raise RuntimeError(f"No frames found in {frames_dir}")

    random.seed(seed)
    kept_images = 0
    kept_boxes = 0
    empty_images = 0
    for img_path in imgs:
        split = "val" if random.random() < val_frac else "train"
        out_img_dir = dataset_dir / split / "images"
        out_lab_dir = dataset_dir / split / "labels"
        out_img_dir.mkdir(parents=True, exist_ok=True)
        out_lab_dir.mkdir(parents=True, exist_ok=True)

        im = Image.open(img_path)
        result = model.predict(source=str(img_path), imgsz=640, conf=conf, iou=iou, verbose=False)[0]
        lines: list[str] = []
        if result.boxes is not None:
            for box in result.boxes:
                cls = int(box.cls[0].item()) if box.cls is not None else 0
                if cls < 0 or cls >= len(CLASSES):
                    continue
                line = yolo_label_line(cls, box.xyxy[0].tolist(), im.width, im.height)
                if line:
                    lines.append(line)
        if not lines:
            empty_images += 1
            continue

        shutil.copy2(img_path, out_img_dir / img_path.name)
        (out_lab_dir / (img_path.stem + ".txt")).write_text("\n".join(lines) + "\n", encoding="utf-8")
        kept_images += 1
        kept_boxes += len(lines)

    yaml = "train: train/images\nval: val/images\nnc: %d\nnames: [%s]\n" % (
        len(CLASSES),
        ", ".join("'%s'" % c for c in CLASSES),
    )
    (dataset_dir / "data.yaml").write_text(yaml, encoding="utf-8")
    return kept_images, kept_boxes, empty_images


def make_review_sheet(dataset_dir: Path, out_path: Path, max_images: int = 36) -> None:
    imgs = sorted((dataset_dir / "train" / "images").glob("*.jpg"))[:max_images]
    if not imgs:
        return
    thumbs: list[Image.Image] = []
    for img_path in imgs:
        im = Image.open(img_path).convert("RGB")
        draw = ImageDraw.Draw(im)
        lab = dataset_dir / "train" / "labels" / (img_path.stem + ".txt")
        if lab.is_file():
            for line in lab.read_text(encoding="utf-8").splitlines():
                p = line.split()
                if len(p) < 5:
                    continue
                cls, cx, cy, w, h = int(p[0]), *map(float, p[1:5])
                x0 = int((cx - w / 2) * im.width)
                y0 = int((cy - h / 2) * im.height)
                x1 = int((cx + w / 2) * im.width)
                y1 = int((cy + h / 2) * im.height)
                color = (255, 40, 60) if cls == 0 else (70, 210, 255)
                draw.rectangle((x0, y0, x1, y1), outline=color, width=3)
                draw.text((x0 + 2, max(0, y0 - 12)), CLASSES[cls] if cls < len(CLASSES) else str(cls), fill=color)
        im.thumbnail((240, 160), Image.Resampling.LANCZOS)
        tile = Image.new("RGB", (240, 160), (18, 18, 20))
        tile.paste(im, ((240 - im.width) // 2, (160 - im.height) // 2))
        thumbs.append(tile)
    cols = 3
    rows = math.ceil(len(thumbs) / cols)
    sheet = Image.new("RGB", (cols * 240, rows * 160), (12, 12, 14))
    for i, tile in enumerate(thumbs):
        sheet.paste(tile, ((i % cols) * 240, (i // cols) * 160))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path, quality=92)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True, help="local video file, e.g. D:/RO/my_run.mp4")
    ap.add_argument("--sample-every", type=float, default=1.0, help="seconds between candidate frames")
    ap.add_argument("--max-frames", type=int, default=1200)
    ap.add_argument("--hash-distance", type=int, default=4, help="lower keeps more similar frames; higher removes more duplicates")
    ap.add_argument("--jpg-quality", type=int, default=92)
    ap.add_argument("--pseudo-label", action="store_true", help="run current YOLO and create labels")
    ap.add_argument("--model", default=str(DEFAULT_MODEL))
    ap.add_argument("--conf", type=float, default=0.65, help="high confidence keeps pseudo labels safer")
    ap.add_argument("--iou", type=float, default=0.45)
    ap.add_argument("--val-frac", type=float, default=0.08)
    ap.add_argument("--stage-trainingdata", action="store_true", help="write pseudo dataset into TrainingData so merge_datasets.py sees it")
    ap.add_argument("--seed", type=int, default=1337)
    a = ap.parse_args()

    src = Path(a.src)
    if not src.is_file():
        raise FileNotFoundError(src)
    stem = slugify(src)
    work = VIDEO_ROOT / stem
    frames = work / "frames"
    if frames.exists():
        shutil.rmtree(frames)

    stats = extract_frames(src, frames, a.sample_every, a.max_frames, a.hash_distance, a.jpg_quality)
    log("[video] source fps=%.2f total_frames=%s kept=%d skipped_near_duplicate=%d" %
        (stats.fps, stats.total_frames or "?", stats.kept, stats.skipped_near_duplicate))
    log("[video] frames -> " + str(frames))

    if not a.pseudo_label:
        log("[video] done. Add --pseudo-label --stage-trainingdata to create YOLO labels.")
        return

    dataset = TRAINING_DATA / f"Video_{stem}.yolov8" if a.stage_trainingdata else work / "pseudo_yolo"
    if dataset.exists():
        shutil.rmtree(dataset)
    kept_images, kept_boxes, empty = pseudo_label(frames, dataset, Path(a.model), a.conf, a.iou, a.val_frac, a.seed)
    make_review_sheet(dataset, work / "review_pseudo_labels.jpg")
    log("[video] pseudo dataset -> " + str(dataset))
    log("[video] labeled_images=%d boxes=%d skipped_empty=%d" % (kept_images, kept_boxes, empty))
    log("[video] review sheet -> " + str(work / "review_pseudo_labels.jpg"))
    if a.stage_trainingdata:
        marker = dataset / "review_required.json"
        marker.write_text(json.dumps({
            "source": str(src),
            "review_sheet": str(work / "review_pseudo_labels.jpg"),
            "note": "Review labels first. Rename or copy this file to approved_for_training.json when the dataset is safe for training."
        }, indent=2), encoding="utf-8")
        log("[video] staged for review. merge_datasets.py will skip it until approved_for_training.json exists.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
