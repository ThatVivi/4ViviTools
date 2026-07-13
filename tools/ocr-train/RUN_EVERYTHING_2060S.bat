@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul
set NO_PAUSE=1
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8
set OCR_CPU_THREADS=8
set PIP_DISABLE_PIP_VERSION_CHECK=1

echo ================================================================
echo   4ViviTools - TRAIN EVERYTHING
echo   Tuned for i7-11700K + RTX 2060 Super 8GB + 32GB RAM
echo ================================================================
echo.
echo This will:
echo   1. Extract all GRF/Claude monsters as full-frame references
echo   2. Train RO text OCR
echo   3. Train skills + monster sprite recognition
echo   4. Rebuild YOLO data with Roboflow screenshots + hard negatives
echo   5. Generate/mix augmented GRF monster scenes
echo      - motion blur, tiny/far monsters, damage numbers, names, HP bars
echo   6. Run Supervision / ByteTrack-ready YOLO dataset QC sheets
echo   7. Train/export YOLO entity detector
echo   8. Build the Release app
echo.
echo Re-run this file if a stage fails; the pipeline resumes where possible.
echo.

echo [1/3] Installing/updating Python training dependencies...
python -m pip install --upgrade pip setuptools wheel
if errorlevel 1 goto err
python -m pip install -r requirements.txt
if errorlevel 1 goto err

echo.
echo [real] Adding optional hard-negative false-positive frames...
python mine_hard_negatives.py
if errorlevel 1 goto err

echo.
echo [2/3] Running preflight checks...
python train_everything_2060s.py --preflight-only --yolo-epochs 60 --yolo-scenes 6000 --imgsz 640
if errorlevel 1 goto err

echo.
echo [3/3] Starting resumable full training...
python train_everything_2060s.py --yolo-epochs 60 --yolo-scenes 6000 --imgsz 640
if errorlevel 1 goto err

echo.
echo [calibration] Checking detector score separation on validation frames...
python check_calibration.py
if errorlevel 1 goto err

echo.
echo ===================== ALL TRAINING DONE =====================
echo Release folder:
echo   ..\..\src\4rVivi.App\bin\Release\net8.0-windows10.0.19041.0
echo.
echo Training log:
echo   last_full_training.log
echo YOLO QC:
echo   yolo_real\qc_supervision\report.json
echo   yolo_real\qc_supervision\train_sample.jpg
echo   yolo_real\qc_supervision\val_sample.jpg
pause
goto :eof

:err
echo.
echo *** Training stopped on a failed stage. Read the log above. ***
echo *** You can run this file again after fixing the error.      ***
pause
