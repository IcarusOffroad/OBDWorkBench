@echo off
if exist "%ProgramFiles%\Icarus\GPS\Tray\IcarusGPS.Tray.exe" (
 start "" "%ProgramFiles%\Icarus\GPS\Tray\IcarusGPS.Tray.exe"
) else (
 echo Run BUILD.bat and INSTALL.bat first.
 pause
)
