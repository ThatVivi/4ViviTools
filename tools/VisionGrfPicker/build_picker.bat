@echo off
setlocal
cd /d "%~dp0"

echo ================================================================
echo   Building VisionGrfPicker.exe (self-contained, one file)
echo ================================================================
echo   gamedata.json and mobid_sprite_map.json are embedded from the
echo   repo automatically - nothing to copy.
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 (
  echo.
  echo BUILD FAILED - read the error above.
  echo Press any key to exit...
  pause >nul
  exit /b 1
)

echo.
echo ================================================================
echo   DONE. Ship just this one file:
echo   %~dp0publish\VisionGrfPicker.exe
echo ================================================================
echo Press any key to exit...
pause >nul
