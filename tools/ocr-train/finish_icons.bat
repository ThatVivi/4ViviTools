@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo  1 GO ALL IN  -  icon embedder training, then ONNX convert
echo ============================================================
echo [1/2] training icon embedder (resumable - re-run this .bat to continue)...
set NO_PAUSE=1
python build_icon_model.py
set "NO_PAUSE="
echo [2/2] converting text + icon models to ONNX in the clean venv...
call convert_in_venv.bat
echo ALL DONE.
pause
