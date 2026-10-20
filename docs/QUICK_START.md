# OBD Workbench Public Test Preview - Quick Start

OBD Workbench v0.5.0.0 is a public-test build. It is intended to collect repeatable diagnostic evidence from vehicles and adapters while the ECU-specific profiles are expanded.

## First connection

1. Connect the diagnostic adapter to the vehicle and computer/phone network as appropriate.
2. Turn the ignition on. Engine-off is preferred for first connection tests.
3. Start OBD Workbench and choose the matching splash-screen tile: **OBDII**, **SSM**, **TD5**, or **GEMS**.
4. Select the COM port or Wi-Fi/TCP endpoint and choose **Connect + Initialize**.
5. Leave automatic recording enabled. Workbench records the session transcript and CSV evidence automatically.
6. If the ECU fingerprint is reported as unknown, keep the session read-only where possible and create a **Support ZIP**.

## During a test

Use the event-note box at the bottom of the main window and click **Mark event** immediately before or after an action such as:

- Disconnected MAF
- Reconnected MAP/IAT
- Started engine
- MIL came on
- Engine cut out
- Ignition off / ignition on

This places a timestamped marker into the diagnostic evidence.

## Reporting a compatibility result

Click **Support ZIP**. The generated ZIP includes the session log, current CSV recording, ECU fingerprint/profile, modules, DTC snapshot, and SSM/TD5/GEMS family-specific evidence. By default it removes the Windows user-profile path, machine name, and VIN.

Attach the ZIP to the relevant GitHub issue rather than pasting a large log into the issue body.

## Numeric calibration and calculated values
For numeric signals, double-click the Live Data row/card, dashboard gauge, or trend configuration to set **Gain**, **Offset**, and display decimal places. Workbench uses `displayed = decoded × gain + offset` while preserving the raw/native ECU value. **Set zero** calculates the offset that makes the current reading zero, which is useful for turning an absolute-pressure reading into a relative/gauge reading.

Right-click a numeric signal and choose **Create new Calculated value…** to make a second signal without changing the original. `A` is the live source value; `B`, `C`, and `D` are constants you can enter. Formulas support numbers, parentheses, and `+ - * /`. Presets are included for °F/°C, mph/km/h/m/s, and RPM/Hz/RPH conversions. Calculated values are logged to CSV and can be pinned, trended, and added to the dashboard.

Dashboard gauges can use a 2–5 column grid and can color configurable low/high sweep ranges. On Trending, drag a rectangle over the graph to zoom into that time/value region; repeat to zoom farther and use **Reset zoom** to return to the normal rolling window.


## GPS and further reading

Open GPS settings, choose COM or Icarus GPS Service, and check Enable GPS in Workbench. Service diagnostics are available before a position fix. See [GPS](GPS.md), [user guide](USER_GUIDE.md), [build instructions](BUILDING.md) and [troubleshooting](TROUBLESHOOTING.md).
