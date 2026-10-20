@echo off
setlocal
cd /d "%~dp0"
dotnet run --project RegressionTests\RegressionTests.csproj -c Release
if errorlevel 1 goto :fail
echo.
echo Portable core checks passed.
pause
exit /b 0
:fail
echo.
echo PORTABLE CORE CHECKS FAILED.
pause
exit /b 1
