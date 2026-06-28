@echo off
setlocal
cd /d "%~dp0"
set NO_PAUSE=1
echo ================================================================
echo   4ViviTools OCR - BUILD ALL  (overnight, hands-off, resumable)
echo ================================================================
echo [1/8] GRF sprites (skips if present)...
python extract_sprites.py
echo [2/8] GRF map minimaps (skips if present)...
python extract_maps.py
echo [3/8] text OCR model...
if exist "work\output\rec_ro\best_accuracy.pdparams" (
  echo   text model already trained - skipping
) else (
  python run.py
)
echo [4/8] icon embedder (train / resume)...
python build_icon_model.py
echo [5/8] synthetic YOLO scenes (skips if present)...
python gen_yolo_scenes.py
echo [6/8] entity detector YOLOv8 (train / resume / export)...
python train_yolo.py
echo [7/8] convert text + icons to ONNX in the clean venv...
if not exist ocr_export\Scripts\python.exe python -m venv ocr_export
call ocr_export\Scripts\activate.bat
python -m pip install --upgrade pip
python -m pip install --no-cache-dir packaging setuptools wheel protobuf==4.25.8 onnx==1.17.0 paddle2onnx==2.0.2rc3 paddlepaddle==3.1.0
python convert_in_venv.py
call ocr_export\Scripts\deactivate.bat 2>nul
echo [8/8] building the app so the trained models go live...
where dotnet >nul 2>nul
if %errorlevel%==0 (
  dotnet build "..\..\4rVivi.sln" -c Release
) else (
  echo   dotnet not found on PATH - open 4rVivi.sln and Rebuild to bake in the models
)
echo ===================== ALL DONE =====================
pause
