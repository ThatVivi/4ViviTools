@echo off
setlocal
cd /d "%~dp0"
set NO_PAUSE=1
echo ================================================================
echo   4ViviTools  -  RUN ALL OCR TRAINING  (GPU+CPU, automatic)
echo   Double-click and leave it. Safe to close + re-run to resume.
echo ================================================================
echo.

echo [1/5] TEXT recognition fine-tune on RO fonts (the big GPU step)...
echo       (renders RO-realistic crops -^> fine-tunes latin PP-OCRv5 rec)
REM --- one-time clean retrain: the old checkpoint was trained on the OLD renderer.
REM     Wipe it ONCE so we train fresh on the new realistic data; later runs just resume.
if not exist ".retrain_v2_smallset.flag" (
  echo   First run on the new realistic data - clearing the old checkpoint for a clean retrain...
  if exist "work\output\rec_ro" rmdir /s /q "work\output\rec_ro"
  echo done> ".retrain_v2_smallset.flag"
)
python run.py --gpu --epochs 6
if errorlevel 1 goto err

echo.
echo [2/5] Convert the trained text model to ONNX (isolated clean venv)...
if not exist ocr_export\Scripts\python.exe python -m venv ocr_export
call ocr_export\Scripts\activate.bat
python -m pip install --upgrade pip
python -m pip install --no-cache-dir packaging setuptools wheel protobuf==4.25.8 onnx==1.17.0 paddle2onnx==2.0.2rc3 paddlepaddle==3.1.0
python convert_in_venv.py
call ocr_export\Scripts\deactivate.bat 2>nul

echo.
echo [3/5] Monster sprite bank (append to icon model, skips ones already added)...
python build_monster_names.py

echo.
echo [4/5] Map minimap bank (append, skips existing)...
python build_map_names.py

echo.
echo [5/5] Rebuild the app so the new models go live...
where dotnet >nul 2>nul
if %errorlevel%==0 (
  dotnet build "..\..\4rVivi.sln" -c Release
) else (
  echo   dotnet not on PATH - open 4rVivi.sln in Visual Studio and Rebuild.
)

echo.
echo ===================== ALL TRAINING DONE =====================
echo  Shipped: latin_PP-OCRv5_rec_mobile_infer.onnx (RO-tuned text)
echo           icon_refs.bin + map_names.json (monsters + maps)
echo  Then just launch 4ViviTools.
echo.
echo  Tip: to force another clean retrain later, delete the file
echo       tools\ocr-train\.retrain_v2_smallset.flag and run this again.
pause
goto :eof

:err
echo.
echo *** A step failed - read the log above. Just re-run this file; it resumes where it stopped. ***
pause
