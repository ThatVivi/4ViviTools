@echo off
setlocal
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8

echo ================================================================
echo   4ViviTools - INGEST GAMEPLAY VIDEO FOR YOLO
echo ================================================================
echo.
echo Drop a local gameplay video path here. Use your own recording or a video
echo you have permission to use.
echo.
set /p VIDEO=Video path: 
if "%VIDEO%"=="" goto err

python ingest_video.py --src "%VIDEO%" --sample-every 1.0 --max-frames 1200 --pseudo-label --stage-trainingdata
if errorlevel 1 goto err

echo.
echo Done. Check:
echo   tools\ocr-train\video_frames
echo.
echo Then run:
echo   RUN_EVERYTHING_2060S.bat
echo.
pause
goto :eof

:err
echo.
echo Failed or no video path entered.
pause
