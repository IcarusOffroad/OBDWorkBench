@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1"
if errorlevel 1 (
 echo Build failed. See the error above and build.log.
 pause
 exit /b 1
)
echo Build complete. Next: run INSTALL.bat.
pause
