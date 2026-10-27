@echo off
cd /d "%~dp0"
if exist "%~dp0.venv\Scripts\python.exe" goto packages
py -3 -c "import sys; assert sys.version_info >= (3, 10)" >nul 2>&1
if not errorlevel 1 (
  py -3 -m venv "%~dp0.venv"
  goto venvcheck
)
python -c "import sys; assert sys.version_info >= (3, 10)" >nul 2>&1
if errorlevel 1 goto nopython
python -m venv "%~dp0.venv"
:venvcheck
if not exist "%~dp0.venv\Scripts\python.exe" goto failed
:packages
"%~dp0.venv\Scripts\python.exe" -m pip install -r "%~dp0requirements.txt"
if errorlevel 1 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 goto failed
if not exist "%~dp0settings.json" copy /y "%~dp0settings.example.json" "%~dp0settings.json" >nul
call "%~dp0Start-hub.cmd" --check
if errorlevel 1 goto failed
echo Ready. Double-click Start-hub.cmd.
if /i not "%~1"=="--no-pause" pause
exit /b 0
:nopython
echo Python 3.10 or newer is required. Install Python and run Setup.cmd again.
goto failed
:failed
echo Setup failed. See the error above.
if /i not "%~1"=="--no-pause" pause
exit /b 1
