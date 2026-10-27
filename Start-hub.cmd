@echo off
cd /d "%~dp0"
if not exist "%~dp0.venv\Scripts\python.exe" goto missing
if not exist "%~dp0WorkbenchHub.exe" goto missing
if not exist "%~dp0backend.py" goto missing
if not exist "%~dp0settings.json" copy /y "%~dp0settings.example.json" "%~dp0settings.json" >nul
if /i "%~1"=="--check" (
  "%~dp0.venv\Scripts\python.exe" -s -c "import psutil"
  if errorlevel 1 exit /b 1
  exit /b 0
)
start "" "%~dp0WorkbenchHub.exe"
exit /b 0
:missing
echo Please run Setup.cmd first.
if /i not "%~1"=="--check" pause
exit /b 1
