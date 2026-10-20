# Workbench r23 — independent Windows GPS connection

Based on r22 LCARS navigation. GPS is off by default. Build using build.bat with the .NET 8 SDK on Windows.

## Use

1. Open Preferences → GPS connection and NMEA debug, or click the GPS indicator at the top of the main window.
2. Select the receiver COM port and serial settings, then enable GPS. Settings save automatically; Workbench reconnects on the next launch. Default is 9600 baud, 8 data bits, no parity, one stop bit, no flow control. Select the COM port Windows assigns to your NMEA receiver. Do not select the diagnostic adapter's port.
3. GPS runs independently of ELM/SSM/TD5/GEMS polling. Temporary failures retry every three seconds. Disabling GPS releases its port after the current read timeout (up to about half a second).
4. The top indicator shows green for a current valid fix, amber for a connected receiver waiting for a fix, red for a connection problem, and gray when disabled. A serial port being open alone does not establish a fix.
5. GPS cards appear in Live Data. Use the existing Add to Trend / Add to Dashboard actions for GPS Speed and other numeric GPS values. Speed follows the normal mph/km/h unit preference. Altitude is metres; coordinates and course are degrees.
6. GPS samples are included automatically whenever the existing CSV recorder is running. Samples use Source=GPS and requests GPS-SPEED, GPS-LATITUDE, GPS-LONGITUDE, GPS-ALTITUDE, GPS-COURSE, GPS-SATELLITES, GPS-HDOP and GPS-FIX. The normal CSV timestamps are PC local time with UTC offset. The per-field raw NMEA sentence retains receiver UTC (RMC also retains receiver date). CSV numeric precision now retains up to eight decimal places. GPS samples can be loaded by the normal replay reader.

## Debug and validity

The configuration screen shows a bounded rolling view of the latest 250 raw lines, receiver time, status, and rejected sentence count. It is read-only; no commands are sent to the GPS. Settings take effect when changed; an edited COM-port field saves on leaving the field or closing the window. GPS changes save independently of the Preferences Apply/Cancel buttons.

Checksummed RMC, GGA and VTG sentences accept GP, GN and other two-character talker identifiers. Other raw sentences remain visible in debug. Missing values remain unavailable. Navigation fields expire after three seconds; invalid fix sentences immediately invalidate navigation. Valid zero speed remains zero. Satellite count and HDOP can remain available without a fix if freshly reported. VTG requires an established current fix. An estimated/dead-reckoned GGA fix is not treated as a valid satellite fix.

Logging publishes current available values at 1 Hz; individual receiver fields may update less often and are held for at most three seconds. GPS status transitions are CSV events. Missing/invalid navigation values do not create fabricated zero samples. Historical gauges retain their last sample and trend lines can connect the samples on either side of a reception gap; use GPS Fix Valid and status events to identify these intervals. Live cards show an em dash while unavailable. Raw NMEA debug is a rolling display, not a separate continuous raw capture file.

GPS configuration is inactive in replay mode. It does not synchronize the PC clock, use Windows Location Services, or provide maps. GPS altitude/course/speed accuracy remains dependent on the receiver and reception conditions.

## Windows receiver acceptance test

- Build and launch; verify GPS disabled by default.
- Select your GPS COM port (the Dell 7204 previously used COM6) at 9600/8-N-1; verify readable NMEA and wait for a fix.
- Verify green indicator and plausible speed/coordinates; add GPS Speed and ECU speed to one trend.
- Start CSV recording; verify Source=GPS samples alongside ECU samples; replay that CSV.
- Close/reopen Workbench; verify GPS settings and automatic reconnect.
- Unplug receiver / remove reception: verify red or amber status, no fresh navigation samples, then automatic recovery.
- Disable GPS and verify another serial terminal can open the released port.
- Repeat with the classic and LCARS skins; verify settings/debug remain accessible.

Automated parser, settings, shared-Core and Windows-target build validation is recorded in GPS_BUILD_VALIDATION.txt. Physical serial reception and Windows visual acceptance require testing on the PC.
