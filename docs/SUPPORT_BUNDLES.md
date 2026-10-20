# Diagnostic Support Bundles

The **Support ZIP** button creates a local ZIP under:

`%LOCALAPPDATA%\OBDWorkbench\SupportBundles`

Public-test bundles are intended to be self-describing. A bundle should never silently look complete when a live log could not be captured.

## Expected contents

- `Manifest.json` - Workbench version, ECU family/profile/fingerprint, adapter/protocol metadata, and capture summary
- `CaptureStatus.txt` - explicit INCLUDED / NOT AVAILABLE status for the session log, CSV recording, snapshots, events and family evidence
- `Session.log` - sanitized command/session transcript when the active logger can be snapshotted
- `OBDLog.csv` - sanitized continuous recording when active/available
- `LiveSnapshot.csv` - the values visible when the bundle was created, including raw data, sample status and timing fields
- `Events.csv` - persistent **Mark event** entries with timestamps, family and ECU fingerprint
- `Identification.txt` - human-readable identity/fingerprint snapshot
- `DetectedModules.csv`
- `DTC_Snapshot.csv`
- `FamilyData.csv` - family-specific evidence such as TD5 bitmap data, GEMS dealer reads, or SSM BF/DTC-bank bytes
- `UserNotes.txt`
- `README.txt`

`Session.log` and `OBDLog.csv` are captured from the active log writers first. If an active snapshot is unavailable, Workbench attempts a shared-read file fallback. If neither works, `CaptureStatus.txt` records the reason instead of silently omitting the evidence.

## Event markers

**Mark event** entries are stored independently for the active Workbench session and exported to `Events.csv`. They are also written to the normal session/CSV logs when those logs are active. This means a note such as `Disconnected MAF`, `MIL came on`, or `Engine cut out` remains in the support bundle even if the rolling log file itself cannot be copied.

## Privacy defaults

The support-bundle copy removes or redacts:

- Windows user-profile path
- computer/machine name
- VIN, including VIN-shaped 17-character identity tokens even when Workbench has not yet decoded the field as a VIN

The original local logs are not modified.

No support bundle is uploaded automatically. The user decides where to attach or send it.

## GPS location privacy

GPS coordinates, receiver timestamps, raw NMEA, service snapshot JSON and route history are **not automatically redacted**. A bundle or CSV can reveal location even when VIN/user paths have been sanitized. Review the entire bundle before posting it publicly; remove recordings or sensitive GPS fields from a copy when needed. Original local logs remain unchanged.
