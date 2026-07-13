@echo off
setlocal
cd /d "%~dp0"
if not exist ocr_export\Scripts\python.exe (
  echo [venv] creating clean export environment...
  python -m venv ocr_export
)
call ocr_export\Scripts\activate.bat
python -m pip install --upgrade pip
python -m pip install --no-cache-dir setuptools wheel packaging protobuf==4.25.8 onnx==1.17.0 paddle2onnx==2.0.2rc3
python convert_in_venv.py
pause
