## v0.2.4

- Added matching Icarus executable/taskbar and status-badged tray icons.
- Normalized package metadata and refreshed build/install documentation.
- Made virtual-output stream disposal lifetime explicit, removing a compiler warning without changing receiver behavior.
- Version-1 data/control protocol and receiver behavior unchanged.

# v0.2.2 — Compact widget

- Put speed and elevation side by side with equally large readings.
- Reduced default height from 270 to 158 logical pixels at the same 300 pixel width.
- Tightened header/padding and reduced satellite/footer font sizes.
- Updated right-click hit areas for the two columns; retained unit, opacity, position and scale preferences.
- Kept the working v0.2.1 desktop-placement behavior and tray-only updater.

# v0.2.1 — Widget visibility recovery

- Removed cross-process Explorer parenting and absolute-bottom window placement, both of which could hide the translucent widget.
- Keep an independent, non-activating panel immediately above the desktop surface and behind application windows.
- Added Recover widget (reset corner, show above applications for 10 seconds), Widget details, and bounded widget error logging.
- Made Show and Hide explicit separate tray commands.
- Added UPDATE_TRAY.bat to replace only the tray/widget while leaving the running GPS service untouched.
- All projects compile; the existing 32 parser/presentation checks pass. Native Windows behavior still needs confirmation on the user's PC.

# v0.2.0 — Desktop GPS widget

- Added translucent desktop panel showing LED status, speed, MSL elevation, satellites used and reported in view.
- Added direct right-click speed/elevation unit selection, opacity, size, position lock/reset, and hide/show.
- Persisted widget preferences separately per Windows user, with mph/feet defaults.
- Added startup via the existing tray sign-in entry, stale-data checks, desktop-host recovery, and monitor-bound checks.
- Retained the same IcarusGPS service identity, receiver settings, and version 1 sharing protocol.
- Added 13 portable widget-presentation checks; all 32 parser/presentation checks pass.
- Windows desktop rendering, right-click behavior, service startup, Explorer restart, and sleep/resume need physical Windows testing.

## 0.2.3 (source update, requires Windows build/verification)
- Tray commands for temporarily releasing and resuming the physical GPS COM port without editing saved configuration.
- Optional NMEA sentence forwarding to a configured serial COM output, suitable for one side of a virtual null-modem pair.
- `VIRTUAL_COM_SETUP.md` with driver-signing caveats, port mapping, update steps, and limitations.
