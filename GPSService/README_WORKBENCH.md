# Icarus GPS Service bundled with OBD Workbench r26

This folder includes **IcarusGPS_v0.2.4**, based on the supplied v0.2.3 service/monitor: Windows service, tray monitor, desktop widget, build/install/update/uninstall scripts, integration reference, and virtual-COM instructions. Version 0.2.4 adds matching Icarus icons and normalizes package metadata/documentation; the data-pipe protocol and receiver behavior remain version 1.

## Already have the service running?

You do not need to reinstall it for Workbench. Open Workbench → Preferences → GPS connection and NMEA debug (or click the GPS status indicator). Select **Icarus GPS Service**, then enable GPS. Selection and enabled state save automatically. Workbench reconnects to this source when it starts again.

The service owns the physical receiver COM port and supplies data to Workbench, the tray/widget and other subscribers simultaneously. **No virtual COM port is needed for Workbench.** It uses the local `Icarus.GPS.Data.v1` feed supported by this bundled service.

## First installation on Windows 10/11 x64

1. Extract the entire Workbench ZIP to a writable folder. Install the .NET 8 SDK if not already present. Internet access is needed for the initial restore/build.
2. Build Workbench using its root `build.bat`.
3. Open `GPSService\IcarusGPS_v0.2.4` and run **BUILD.bat**. This separately tests and publishes the GPS service and tray/widget into that folder's `publish` directory. The GPS applications are self-contained after publishing.
4. Close PuTTY and any other program directly using the GPS receiver COM port. If Workbench currently uses Direct COM, disable GPS there before installing/starting the service.
5. Run **INSTALL.bat** in that GPS folder and accept the administrator prompt. It installs **Icarus GPS Service** (service name `IcarusGPS`) for automatic startup and configures the tray application to launch at Windows sign-in.
6. Run **OPEN_MONITOR.bat**. In the GPS monitor's Connection settings, select the physical GPS receiver COM port and baud rate. The default is COM6 at 9600, 8-N-1; use the actual port shown on your PC. Saving machine receiver settings requires administrator approval.
7. Allow the receiver to acquire a fix. In Workbench select **Icarus GPS Service** and enable GPS. The indicator turns green with a valid current fix. GPS cards can be added to trends and gauges using their normal context menu, and enter the normal CSV datalog whenever recording is running.

Building Workbench does not automatically build or install the service. Building or enabling GPS in Workbench does not require administrator rights. Only the separate service installation/configuration does.

## Source selection and ownership

- **Icarus GPS Service:** COM settings are disabled in Workbench. Change the receiver's hardware port/baud in the GPS tray monitor. Workbench subscribes read-only and does not stop, release, resume or reconfigure the service. Turning off GPS in Workbench only disconnects its own subscription; the service and other consumers keep running.
- **Direct COM port:** Workbench opens the selected physical (or separately configured virtual NMEA) port directly using its saved serial settings. Before selecting the physical receiver port, use the GPS tray monitor's release option, or disable its receiver connection. Otherwise Windows denies access because the service holds the port. Source selection does not automatically release it. Resume the service when switching back to service mode.
- Source choice and serial settings are saved independently; switching to Service does not require a COM port or erase the saved direct-port configuration.

## Status, debug, logging and recovery

Green means a fresh valid GPS fix, amber means connected to the service but its receiver is disabled/released/disconnected or waiting for a fix, red means the service feed itself is unavailable, and gray means Workbench GPS is off. Service details and the receiver's own message appear in the debug screen. A running Windows service alone does not prove a valid GPS fix.

The debug screen shows service publication UTC, receiver GPS UTC, receiver port/baud, valid/rejected counts, and recent NMEA lines from the service. It does not open a second serial connection. The upstream service retains the last 20 lines, rather than Workbench's direct-COM 250-line buffer.

Workbench reconnects every two seconds after a failed subscription (initial connection timeout up to three seconds). Disconnects, unsupported protocol versions, stale publications and five seconds without heartbeats invalidate current service measurements. Receiver fix validity and nullable values are respected; missing speed is not replaced with zero. Published UTC must be within five seconds of PC UTC (up to five seconds of future clock tolerance).

GPS channel IDs remain GPS-SPEED, GPS-LATITUDE, GPS-LONGITUDE, GPS-ALTITUDE, GPS-COURSE, GPS-SATELLITES, GPS-HDOP and GPS-FIX, with CSV Source=GPS. Source/status changes are logged as events. Service samples retain service publication time, receiver GPS UTC and original snapshot measurements in the CSV RawResponse field as JSON. CSV Timestamp remains Workbench's normal PC observation time. These fields allow both clocks to be recovered without changing the existing replay CSV schema. Satellite count is satellites **used**, not a sum of overlapping satellites-in-view counts. GPS speed follows the normal mph/km/h preference; altitude remains metres.

Current cards show unavailable navigation after loss; historical gauges retain the last value and trends can connect samples across gaps, as in r23. Trend GPS Fix Valid and inspect GPS status events to identify these intervals. Service and COM modes may differ in freshness rules: the bundled service uses its documented five-second receiver validity policy, while direct COM retains r23's three-second parser policy.

## Troubleshooting and maintenance

- **Service unavailable:** check Windows Services for **Icarus GPS Service** / `IcarusGPS` and start it. Ensure the installed service supports `Icarus.GPS.Data.v1` protocol version 1. Workbench does not start the service automatically.
- **Service connected but receiver released:** resume acquisition in the GPS tray monitor. If disabled, enable its receiver connection there.
- **No fix:** inspect service NMEA diagnostics and give the receiver clear sky/time to acquire. A connected COM port is not sufficient.
- **COM access denied:** close the other direct-port consumer or release/disable the receiver in the service monitor.
- **Update:** run the bundled GPS BUILD.bat then INSTALL.bat. The installer replaces the same service and preserves its saved receiver configuration. Workbench resumes its subscription automatically.
- **Remove service:** run the bundled UNINSTALL.bat with its administrator prompt. Workbench remains usable with Direct COM.
- Service program files: `%ProgramFiles%\Icarus\GPS`. Settings and connection log: `%ProgramData%\Icarus\GPS`.

For further upstream instructions see `IcarusGPS_v0.2.4\README.md`, `Integration\README.md`, and `VIRTUAL_COM_SETUP.md`. Virtual serial setup is only needed for applications that cannot subscribe directly, such as legacy GPS consumers; it is optional and requires a separate virtual-port driver.
