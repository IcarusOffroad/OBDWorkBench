# Validation status — v0.5.0.0-r30

## r30 build launcher

The batch/helper paths, required project files and Windows CRLF line endings were checked. The source/documentation validator passes. The Windows batch/PowerShell launcher cannot be executed in this Linux environment; end-to-end launcher verification is outstanding. Earlier Release compilation results below apply to the unchanged application code. build.log is ignored by Git and is not bundled.

## r29 baseline

## r29 continuous artwork

Windows-target Workbench Release build passed with no reported warnings/errors; source/documentation checks passed. Splash XAML now contains one Image and no separately cropped ImageBrush backgrounds. Full-image width/height and header spacing derive from one scale, preserving the original aspect ratio and preventing an independently scaled lower section. r28's centered selection panel is retained. Restored/maximized/narrow/high-DPI Windows visual acceptance remains unverified here. This is a layout-only change; Core and GPS baseline tests below were not rerun.

## r28 baseline

## r28 splash layout

Windows-target Workbench Release build passed with no reported warnings/errors. Source/documentation/icon validation passed. The selection border now uses centered/top alignment and a maximum width sized for the six-button row plus padding/DPI rounding allowance; it no longer stretches across the window. Narrow layouts still wrap. Visual acceptance on Windows (restored, maximized, narrow and high-DPI windows) remains a manual check. This is a XAML/layout change; earlier Core and GPS results below were not rerun.

## r27 baseline

## r27 UI fix

Windows-target Workbench Release build passed with no reported warnings/errors. Source/documentation/icon checks passed. Replay retains an accessible collapse toggle and preserves panel state during skin refresh. Replay adapter controls are read-only. Windows visual acceptance (toggle after recording load, both skins, reopen/reload) remains a manual check. Core and GPS receiver/service behavior were not changed, so the earlier results below are retained as baseline results rather than newly rerun tests.

## r26 baseline

Checks performed 2026-10-20 UTC with .NET SDK 8.0.415 in a Linux build environment.

| Check | Result |
| --- | --- |
| Windows-target Workbench solution, Release | Passed; no reported warnings/errors |
| GPS monitor (WinForms), Release | Passed; no reported warnings/errors |
| GPS Windows service, Release | Passed; no reported warnings/errors |
| Shared-Core regression suite, Release, --no-ipc | 190 checks passed |
| GPS parser/widget suite, Release | All 32 checks passed |
| Icon files | SVG/PNG preview inspected; ICO frames/copies verified at required sizes |
| Documentation/source validator | Required guides, Markdown file targets, source hygiene and icon-copy checks passed |
| Runtime content inspection | Docs, license/notices, reference table, branding and independently buildable GPS service folder are present in the Workbench output |
| Source ZIP | Build artifacts, temporary WPF projects, private captures and logs excluded |

Six IPC checks are **unverified**, not passing: five GPS named-pipe integration checks and one TCP loopback check. This environment denies the local sockets used by those checks. The normal test command and GitHub workflow keep them enabled; run the Windows release suite without --no-ipc.

The Windows user confirmed that Workbench's service subscription displays receiver state and raw NMEA indoors before a location fix once Enable GPS in Workbench is checked. This confirms that reception path, not position/speed accuracy or all reconnect conditions.

Windows GUI/taskbar/icon-cache appearance, high-DPI presentation, installer/service execution and physical adapter/receiver/vehicle acceptance still require the release machine. The icon preview was inspected at 16/24/32/48 pixels; compilation is not a substitute for a Windows taskbar test. Independent GEMS vehicle validation remains outstanding.

The GitHub Actions workflow is prepared but has not been executed on GitHub. No repository, public tag or binary release was created during this preparation. Complete the [release checklist](RELEASING.md), including source/provenance review, before publishing a binary release.
