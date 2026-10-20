# GPS in Workbench

## Select and enable a source

Open Preferences → GPS connection and NMEA debug, or click the GPS indicator at the top of the workspace. Choose Direct COM port or Icarus GPS Service, then check **Enable GPS in Workbench**. The checkbox is required even if the service is already running. Both the source and COM settings save for the next launch.

| Source | Ownership and configuration |
| --- | --- |
| Direct COM port | Workbench opens the selected NMEA serial port. Configure COM/baud/data bits/parity/stop bits here. Default 9600/8-N-1, no flow control. |
| Icarus GPS Service | Workbench subscribes read-only to the local service; set receiver port/baud in its tray monitor. No virtual COM port is needed. |

A physical COM port has one owner. To use Direct COM while the service owns the receiver, release it in the tray monitor first. Workbench does not stop/reconfigure the service automatically. Disabling GPS in Workbench leaves the independent service and other subscribers running.

## Status before and after a fix

Service connection status, receiver port, fresh/open/valid flags, satellite information, acquisition time, counters and recent NMEA can appear **without a position fix**. Position, course, speed and altitude remain unavailable when not valid/fresh. Zero satellites and blank coordinates indoors are plausible, but a blank Workbench debug screen labeled disabled means Workbench is not subscribed.

Gray = Workbench GPS off. Green = current valid fix. Amber = connected but no usable fix (including a disabled/released service receiver). Red = direct serial connection or service subscription unavailable. The standalone GPS tray uses its own gray/amber/green receiver status convention.

## Signals, recording and validity

GPS Speed, Latitude, Longitude, Altitude, Course, Satellites, HDOP and Fix Valid appear as source GPS. Add numeric signals to gauges/trends like ECU data. Speed follows the normal mph/km/h preference. Satellite count means satellites used; overlapping GP/GL/GN in-view totals are not added together.

Samples join normal CSV recording at 1 Hz. CSV retains native/display values and enough precision for coordinates. The service's PublishedUtc and receiver GpsUtc are retained in RawResponse JSON; direct COM raw NMEA retains receiver time/date when available. Source/status transitions are log events. Receiver time does not change the PC clock.

Direct COM checks RMC/GGA/VTG checksums and expires navigation after three seconds. Service mode uses the service's nullable fields/validity, rejects incompatible or stale snapshots, and invalidates measurements on disconnect or five seconds without a heartbeat. Missing speed is not changed to zero. Historical gauges/trends can retain/connect old samples across a gap; use GPS Fix Valid and status events to distinguish this from fresh data.

## Install/update the optional service

Use the [bundled installation guide](../GPSService/README_WORKBENCH.md). The service starts at Windows boot; its monitor/widget start at sign-in. To install the refreshed GPS icons, rebuild its package and run INSTALL.bat (or UPDATE_TRAY.bat for a tray-only update), then OPEN_MONITOR.bat. Saved receiver settings are retained by the installer. Rebuilding only Workbench does not replace an installed GPS monitor.

See [troubleshooting](TROUBLESHOOTING.md) and the [service wire protocol](../GPSService/IcarusGPS_v0.2.4/Integration/README.md).
