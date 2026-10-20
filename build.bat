@echo off
setlocal
title Icarus Offroad - OBD Workbench Build
echo Icarus Offroad - OBD Workbench source build
echo Please extract the complete ZIP before running this file.
echo.
if not exist "%~dp0scripts\Build-Workbench.ps1" goto missing
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-Workbench.ps1"
set "workbench_build_exit=%errorlevel%"
goto finish
:missing
echo BUILD FAILED: scripts\Build-Workbench.ps1 is missing.
echo Extract the complete source package into a new folder, then retry.
set "workbench_build_exit=1"
:finish
echo.
echo Build window will remain open so you can read the result.
pause
exit /b %workbench_build_exit%
