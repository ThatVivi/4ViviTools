@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul

set NO_PAUSE=1
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8
set OCR_CPU_THREADS=8
set PIP_DISABLE_PIP_VERSION_CHECK=1
set KEEP_AWAKE_LOCK=%CD%\overnight_yolo_keep_awake.lock

echo ================================================================
echo   4ViviTools - OVERNIGHT YOLO DETECTOR RETRAIN
echo   Tuned for i7-11700K + RTX 2060 Super 8GB + 32GB RAM
echo ================================================================
echo.
echo This run refreshes the monster/entity detector path:
echo   1. Install/update training dependencies
echo   2. Run preflight checks
echo   3. Rebuild Roboflow + video + synthetic YOLO data
echo   4. Generate Supervision / ByteTrack-ready QC sheets
echo   5. Fresh-train/export YOLO entity.onnx
echo   6. Run real-frame score calibration
echo   7. Build the Release app
echo.
echo It keeps the already-trained text OCR and skill/monster embedder.
echo Re-run this file if Windows sleeps or a stage fails; it resumes where possible.
echo.

type nul > "%KEEP_AWAKE_LOCK%"
start "4ViviTools keep-awake" /min python keep_awake.py "%KEEP_AWAKE_LOCK%"

echo [1/3] Installing/updating Python training dependencies...
python -m pip install --upgrade pip setuptools wheel
if errorlevel 1 goto err
python -m pip install -r requirements.txt
if errorlevel 1 goto err

if exist "..\..\..\rathena-master\npc" (
  echo.
  echo [map] Refreshing map-to-monster focus data from local rAthena checkout...
  python ..\build_map_mobs.py --source "..\..\..\rathena-master" --mode both --out "..\..\src\4rVivi.Core\Data\map_mobs.json"
  if errorlevel 1 goto err
)

echo.
echo [real] Pre-labeling optional real gameplay frames...
python label_real.py
if errorlevel 1 goto err

echo.
echo [real] Adding optional hard-negative false-positive frames...
python mine_hard_negatives.py
if errorlevel 1 goto err

echo.
echo [2/3] Running preflight checks...
python train_everything_2060s.py --preflight-only --skip-text --skip-icons --yolo-epochs 100 --yolo-scenes 9000 --imgsz 640 --min-yolo-map50 0.70 --min-yolo-map5095 0.35
if errorlevel 1 goto err

echo.
echo [3/3] Starting overnight YOLO retrain...
python train_everything_2060s.py --skip-text --skip-icons --reset-yolo-checkpoints --fresh-yolo-train --yolo-epochs 100 --yolo-scenes 9000 --imgsz 640 --min-yolo-map50 0.70 --min-yolo-map5095 0.35
if errorlevel 1 goto err

echo.
echo [calibration] Checking detector score separation on validation frames...
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format HHmm"') do set CAL_STAMP=%%i
python check_calibration.py > "..\..\docs\superpowers\calibration-%CAL_STAMP%.txt"
if errorlevel 1 goto err
type "..\..\docs\superpowers\calibration-%CAL_STAMP%.txt"

echo.
echo ===================== OVERNIGHT YOLO DONE =====================
echo Release folder:
echo   ..\..\src\4rVivi.App\bin\Release\net8.0-windows10.0.19041.0
echo.
echo Training log:
echo   last_full_training.log
echo YOLO run:
echo   yolo_real\runs\entity
echo YOLO QC:
echo   yolo_real\qc_supervision\report.json
echo   yolo_real\qc_supervision\train_sample.jpg
echo   yolo_real\qc_supervision\val_sample.jpg
del /q "%KEEP_AWAKE_LOCK%" >nul 2>nul
pause
goto :eof

:err
echo.
echo *** Overnight YOLO run stopped on a failed stage. ***
echo *** Read last_full_training.log, fix the error, then run this file again. ***
del /q "%KEEP_AWAKE_LOCK%" >nul 2>nul
pause
