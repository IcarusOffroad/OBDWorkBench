# Icarus GPS 0.2.3: releasing the GPS receiver and exporting NMEA

## Updated functions

* Tray right-click -> **Release physical GPS port for Google Earth**. This temporarily closes the physical GPS receiver connection, WITHOUT changing the saved Enabled setting. Windows UAC confirmation is required because the service control pipe is administrator-only.
* Tray right-click -> **Resume GPS receiver**. This reopens the receiver. Wait a few seconds after releasing before starting Google Earth, and close Google Earth's GPS session before resuming.
* GPS Monitor -> **Connection settings** -> **Broadcast NMEA to virtual COM pair**. This writes complete original NMEA sentences to a SECOND serial device/port. This does not create virtual ports by itself.
* The GPS service and tray still start at Windows startup; saved GPS configuration persists. The temporary release state does NOT persist through a service restart or reboot.

## Virtual COM pair setup

Illustrative mapping:

    Physical GPS receiver COM6 --> Icarus GPS service --> virtual COM15 == COM16 --> Google Earth Pro

1. Install a trusted, Windows-version-compatible virtual null-modem / paired COM-port driver. Use a properly signed driver; do NOT disable Windows driver-signature enforcement or Secure Boot merely for this feature.
2. Create one connected port PAIR and assign the two ENDS as COM15 and COM16 (or free COM numbers of your choice). Check Device Manager for conflicts. Many virtual port drivers do not work on all Windows builds.
3. In GPS Monitor > Connection settings, leave **GPS receiver COM port** on COM6 (or your actual hardware COM), check **Broadcast NMEA to virtual COM pair**, set Icarus output to **COM15**, and set baud to **9600**. Click Save settings and approve UAC.
4. In Google Earth Pro, use **Tools -> GPS -> Realtime -> NMEA**, and connect to **COM16**, if the Google Earth version/driver allows choosing it. Some versions auto-detect ports instead of presenting a chooser.
5. Icarus GPS service remains connected to COM6 and writes each completed NMEA sentence to COM15; Google Earth reads the paired COM16.

**Critical:** COM15 and COM16 are two ends of ONE PAIR; they are not independently readable clones. One app writes the first end and one app reads the second. Other applications can continue using the Icarus service data pipe, but TWO separate external serial readers cannot usually open COM16 at the same time. For multiple external serial consumers, create additional virtual pairs and extend Icarus to broadcast to each (not included in v0.2.3), or use a multi-client serial splitter.

If the service reports no virtual port output, see `%ProgramData%\Icarus\GPS\service.log`. A virtual-output failure is logged and the physical receiver should continue working. Restart/reconfigure the service to retry opening that output port. Do not assign the virtual output to the physical COM port.

## Driver recommendations

* Free/open source com0com: https://sourceforge.net/projects/com0com/ ; the 2017 signed driver has Windows 10 driver-signature limitations, especially on newer/Secure Boot configurations. Install ONLY if accepted normally by Windows. Avoid instructions asking you to disable Secure Boot or enable test signing.
* A currently supported third-party virtual serial driver with standard Windows driver signing is an alternative. Confirm license and compatibility before purchase.

Do not download COM-port kernel drivers from unverified mirror sites.

## Build and install from source

1. Install Microsoft .NET 8 SDK for Windows.
2. Extract this ZIP. Run `BUILD.bat` and ensure tests and both publishes succeed.
3. Run `INSTALL.bat` as administrator. It stops/updates/restarts the IcarusGPS Windows service, keeps `%ProgramData%\Icarus\GPS\settings.json`, and configures tray/widget auto-start at sign-in.
4. Run `OPEN_MONITOR.bat` (or sign out/in) to launch the updated tray.

**Validation note:** Source modifications have not been compiled or tested on Windows in this environment. Build and smoke-test before relying on the installation. Preserve the previously working v0.2.2 ZIP as a rollback.
