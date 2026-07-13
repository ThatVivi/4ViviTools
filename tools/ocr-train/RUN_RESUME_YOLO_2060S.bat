@echo off
setlocal
cd /d "%~dp0"

echo ================================================================
echo 4ViviTools - Resume YOLO Training (RTX 2060 Super 8GB)
echo ================================================================
echo This resumes from .full_training_state.json and keeps partial
echo synthetic scene images. Use RUN_OVERNIGHT_YOLO_2060S.bat only
echo when you intentionally want to reset YOLO stages 09-13.
echo.

python train_everything_2060s.py --skip-text --skip-icons --fresh-yolo-train --yolo-epochs 100 --yolo-scenes 9000 --imgsz 640 --min-yolo-map50 0.70 --min-yolo-map5095 0.35

echo.
echo Done. Check last_full_training.log for details.
pause
