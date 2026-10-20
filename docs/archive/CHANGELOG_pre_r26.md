# Changelog

### Public-test r12 Jeep/ELM recovery hardening
- Restored the dashboard value progress arc while making configured low/high warning bands substantially thicker so both remain visible together.
- Timed-out ELM operations are explicitly interrupted and synchronized back to the `>` prompt before another command is sent; failure to recover the prompt stops further traffic.
- Once a protocol has been confirmed, recovery no longer falls back to `ATSP0` automatic multi-protocol search.
- Generic Mode 04 clear is blocked while the link is unstable/recovering.
- Mode 03 responder expectations are learned from actual DTC responders rather than every Mode 01 module.

### Public-test r8 engineering/UI build
- Added per-signal Gain + Offset calibration with `Displayed = decoded × Gain + Offset`, Set zero, and fixed decimal-place formatting.
- Added user-created calculated values with safe A/B/C/D arithmetic expressions and common temperature/speed/RPM conversion presets; calculated values are CSV logged and can be gauged/trended.
- Added 2/3/4/5-column dashboard grid selection, denser gauge tiles, and configurable low/high colored gauge sweep bands.
- Added repeated drag-rectangle trend zoom, Reset zoom, and optional recorded-sample dots.
- Moved main navigation inline with the title, reordered DTC/Modules/PIDs/Console, and added explicit touchscreen tab selection.
- Polling Priority now groups one ECU request into one row and shows its decoded plain-English signals.
- Promoted TD5 PID 0x07 Manifold Absolute Pressure from candidate to known.
- Added themed ToolTips to correct unreadable/white hover boxes.
- Existing saved presentation settings are not migrated for this preview; reconfigure them as needed.

### Public-test r7 presentation adjustment
- Added a persistent per-signal numeric display offset, stored by signal/source and display unit.
- Double-click a numeric Live Data row/card/pinned reading to set or reset its offset.
- Gauge and trend configuration rows now include Offset; changing it propagates to matching live/gauge/trend views.
- Existing gauge/trend samples shift immediately when an offset changes, while ECU/raw values remain unchanged.
- CSV NativeValue remains the ECU/native value; display Value includes the configured offset.
- Support bundles record adjusted value, unadjusted numeric value, and display offset separately.


## 0.5.0.0 r5 - Public Test refinement

- Integrated the previously validated Subaru SSM DTC-bank reader into Workbench.
- Added guarded SSM engine-fault-memory reset for the validated SSM fingerprint only, with two fresh zero-RPM/speed checks, one-shot write, reconnect and reread.
- Support ZIPs now self-report capture completeness and preserve active `Session.log`, `OBDLog.csv`, `LiveSnapshot.csv`, and persistent `Events.csv` evidence.
- Engine-speed and vehicle-speed gauges use a zero baseline rather than generating a negative lower scale.
- Gauge/trend Min/Max fields can be typed directly while Autoscale is selected; typing disables Autoscale automatically and incomplete/invalid range pairs are highlighted red.
- Trend rolling-window changes are applied by the configuration window's bottom **Apply** / **Apply & Close** controls; the redundant adjacent Apply button was removed.
- Added a dependency-free custom HSV/RGB color picker below the preset signal colors.
- Increased the Live Data table header area so the Signal search field is not clipped and centered the favorites star column.

## 0.5.0.0 - Public Test Preview

- Added **GEMS** splash-screen session tile.
- Added initial GEMS ISO 14230 dealer-session authentication and recovered dealer-data reads.
- Added GEMS ECU fingerprinting and conservative unknown-response handling.
- Integrated validated TD5 `0x3B` 35-byte fault decoding.
- Expanded TD5 fault catalog with published mappings plus separately labeled experimental evidence.
- Integrated one-shot TD5 `31/DD` fault clearing for the validated early-TD5 fingerprint only.
- Added TD5 static-identity fingerprinting and unknown-profile write gating.
- Added automatic early recording for TD5/GEMS identification data.
- Added timestamped **Mark event** notes.
- Added privacy-sanitized **Support ZIP** generation.
- Added local TD5/GEMS connection, testing, adapter, limitation and protocol-reference documentation.
- GEMS immobilizer synchronization and adaptive reset remain intentionally disabled pending vehicle validation.
