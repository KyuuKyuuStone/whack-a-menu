@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. See the message above.
  pause
  exit /b 1
)
echo.
echo Built: dist\ContextMenuCleaner.exe
pause
