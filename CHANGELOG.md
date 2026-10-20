# Changelog

## v0.5.0.0-r30 — Windows build launcher diagnostics

- build.bat now invokes a Windows PowerShell 5.1-compatible helper, targets OBDWorkbench.sln explicitly, checks required projects/SDK availability, and writes a persistent build.log.
- The launcher keeps the result window open and reports missing/incompletely extracted source or SDK failures clearly.
- Root batch launchers now have CRLF line endings. The underlying .NET restore/build commands and application behavior are unchanged.

## v0.5.0.0-r29 — continuous splash artwork

- Replaced the independently cropped/scaled splash backgrounds with one uniformly scaled, continuous image behind the controls.
- Header spacing derives from the same image scale, keeping terrain and circuit lines aligned while preserving the centered selection panel from r28.

## v0.5.0.0-r28 — splash selection panel

- Centered and bounded the gray session-selection panel to the natural six-button row width instead of stretching it across the window.
- Restored and maximized windows keep the same selection-panel dimensions when there is room for the complete row; narrow windows still allow wrapping.

## v0.5.0.0-r27 — replay connection panel

- Replay starts with the connection panel collapsed and keeps the external collapse/expand control available.
- Skin/layout refreshes preserve replay's panel state instead of reopening the panel in the classic skin.
- Expanded connection controls are read-only in replay. Live connection behavior is unchanged.

## v0.5.0.0-r26 — public-source preparation

- Added matching feather/gear Workbench and feather/compass GPS application icons, with 16–256 pixel ICO frames and editable SVG originals. GPS tray icons retain offline/acquiring/fix badges.
- Embedded icons in executables, Workbench/splash windows and the GPS monitor. Bundled GPS package metadata is now v0.2.4; its receiver/data-pipe behavior is unchanged.
- Moved user documentation to `docs/` while preserving the installed `Docs/` help location. Archived historic patch notes under `docs/archive/`.
- Removed generated build folders, temporary WPF project files and stale machine-specific build reports from the distributable source tree.
- Refreshed README, build/GPS/user/troubleshooting guides, provenance, validation and release documentation; added contribution/security guidance, ignore rules, issue templates and Windows CI configuration.
- Added MIT licensing for project-authored work, retaining existing third-party notices and factual-reference provenance.

## r25

Preserved splash artwork proportions; clarified Workbench GPS enabled state and no-fix service diagnostics.

## r24

Added saved Direct COM / Icarus GPS Service selection, a read-only named-pipe subscriber, stale/invalid-data handling, and the bundled GPS service source/install guide.

## r23

Added independent NMEA GPS serial connection, saved port settings, status indicator, debug data and GPS live/trend/logging channels.

## Earlier v0.5 previews

Generic OBD/SSM/TD5/GEMS paths, guarded ECU-specific writes, support bundles, event markers, calibration/calculated values, dashboards, trends, CSV replay and optional LCARS skin. See the retained [historic patch notes](docs/archive/README.md) for detailed development records.
