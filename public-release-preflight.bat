@echo off
setlocal
cd /d "%~dp0"

echo OBD WorkBench v0.5.0.0 public-release preflight
echo.
dotnet --version
if errorlevel 1 goto :fail

echo.
echo [1/4] Restoring and building Release...
dotnet restore OBDWorkbench.sln
if errorlevel 1 goto :fail
dotnet build OBDWorkbench.sln -c Release --no-restore
if errorlevel 1 goto :fail

echo.
echo [2/4] Running portable regression suite...
dotnet run --project RegressionTests\RegressionTests.csproj -c Release --no-restore
if errorlevel 1 goto :fail

echo.
echo [3/4] GPS parser/widget tests and application builds...
dotnet run --project GPSService\IcarusGPS_v0.2.4\Tests\Tests.csproj -c Release
if errorlevel 1 goto :fail
dotnet build GPSService\IcarusGPS_v0.2.4\Tray\Tray.csproj -c Release
if errorlevel 1 goto :fail
dotnet build GPSService\IcarusGPS_v0.2.4\Service\Service.csproj -c Release
if errorlevel 1 goto :fail

echo [4/4] Source/build checks passed. Run python scripts\check-release.py if Python is installed.
echo.
echo NEXT MANUAL CHECKS:
echo  - Follow docs\RELEASING.md and record exact Windows build/test results.
echo  - Confirm Workbench/splash/taskbar icons and GPS monitor/tray badge icons at multiple DPI settings.
echo  - GPS: check enable checkbox, COM vs Service source, no-fix NMEA diagnostics, persistence and CSV/replay.
echo  - Review license/notices/provenance and ensure no private captures are committed.
echo  - Launch GUI and confirm OBDII / SSM / TD5 / GEMS / Replay splash tiles.
echo  - Calibration: test Gain, Offset, Set zero, reset and decimal places on Live Data and a gauge.
echo  - Calculated values: create A-B and one conversion preset; confirm Live/Dashboard/Trend/CSV operation.
echo  - Dashboard: test 2/3/4/5 columns, compact two-row layout, gauge low/high color bands and dragging.
echo  - Trending: enable sample dots, drag-select to zoom twice, then Reset zoom.
echo  - Touchscreen: tap all top navigation buttons and verify first-touch content switching.
echo  - Preferences / Polling priority: confirm one row per request and plain-English decoded signal descriptions.
echo  - SSM: connect with KKL; confirm BF discovery/live values, DTC read and Support ZIP evidence.
echo  - Known TD5: confirm Manifold Absolute Pressure is known, read DTCs, and create Support ZIP.
echo  - GEMS: real-vehicle test still required before calling GEMS vehicle-validated.
echo  - Inspect Support ZIP: CaptureStatus.txt, Session.log, OBDLog.csv, LiveSnapshot.csv and Events.csv should be present or explicitly reported unavailable.
echo.
echo.
echo PRE-FLIGHT PASSED.
pause
exit /b 0

:fail
echo.
echo PRE-FLIGHT FAILED. Do not tag a public binary release until the error above is resolved.
pause
exit /b 1
