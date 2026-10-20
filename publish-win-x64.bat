@echo off
setlocal
cd /d "%~dp0"
dotnet publish .\OBDWorkbench\OBDWorkbench.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :fail
echo.
echo Publish complete.
echo Folder: OBDWorkbench\bin\Release\net8.0-windows\win-x64\publish\
pause
exit /b 0
:fail
echo.
echo PUBLISH FAILED.
pause
exit /b 1
