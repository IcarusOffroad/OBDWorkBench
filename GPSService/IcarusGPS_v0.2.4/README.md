# Icarus GPS Service & Monitor v0.2.4

Windows GPS receiver service, tray monitor and compact desktop widget. Based on v0.2.3, this revision adds Icarus feather/compass executable, taskbar and status-badge icons and normalizes the package version. Receiver behavior and the version-1 named-pipe interface are unchanged.

## Build and install

Windows 10/11 x64 with the .NET 8 SDK and initial internet access is required to build. Run BUILD.bat, then INSTALL.bat and accept administrator elevation. BUILD tests the parser/widget and publishes self-contained Service and Tray applications. The installer preserves saved receiver settings, installs IcarusGPS under LocalService for automatic boot startup, and adds the tray/widget to Windows sign-in startup. Run OPEN_MONITOR.bat to start them now. For ARM64, use Build.ps1 -Runtime win-arm64.

Program files install to `%ProgramFiles%/Icarus/GPS`; machine settings/logs are in `%ProgramData%/Icarus/GPS`. Default receiver is COM6 at 9600, 8-N-1, DTR/RTS off; select the actual receiver port in Connection settings. Saving requires administrator approval. Close other direct-COM consumers before opening that receiver.

## Use and sharing

The service maintains the receiver connection while the monitor window is closed. Close hides to tray; Exit monitor and widget stops only those UI components. The service still runs. It cannot operate while Windows is asleep. A warm/open receiver can still require time and reception to acquire satellites.

Workbench uses the read-only Icarus.GPS.Data.v1 subscription; no virtual serial port is required. See [Integration/README.md](Integration/README.md) for the snapshot schema and [VIRTUAL_COM_SETUP.md](VIRTUAL_COM_SETUP.md) for legacy applications such as Google Earth. Release/resume actions in the tray let a direct-COM application temporarily own the physical receiver.

The monitor displays fix/port status, latitude/longitude, speed, altitude, course, satellites used/in view, HDOP/quality, receiver UTC, acquisition time and recent NMEA. Unknown values remain blank. Gray/amber/green badges mean disconnected/disabled, acquiring/open without fix, and valid fix. Data can arrive before a position fix.

The desktop widget starts at sign-in by default, behind application windows. Right-click to change units, scale/opacity or show/hide it. The tray has recovery/reset options if it is off-screen. Widget preferences live under `%LOCALAPPDATA%/Icarus/GPS`; errors are logged there as widget-errors.log.

## Update and uninstall

Rebuild then run INSTALL.bat to update both Service and Tray while preserving receiver settings. For only the new monitor/widget/icons, run BUILD.bat → UPDATE_TRAY.bat → OPEN_MONITOR.bat; this does not replace the service. Unpin/re-pin old taskbar shortcuts if Windows caches a generic/old icon. UNINSTALL.bat removes the installed service/startup integration with administrator approval; review its script for retained settings.

## Privacy, interface and validation

Interactive local users can read the GPS pipe, including position and recent NMEA; configuration/control is restricted to administrators/service accounts. There is no internet listener. The service keeps a bounded recent NMEA buffer and connection/error logs, rather than continuous location history. Workbench can record its snapshots separately. Review location data before sharing.

Run `dotnet run --project Tests/Tests.csproj -c Release`. See [VALIDATION.txt](VALIDATION.txt) for current build/test limits. This is source, not a prebuilt installer. Retain LICENSE and THIRD_PARTY_NOTICES.md with distributions. Assets are bundled so this folder remains independently buildable.
