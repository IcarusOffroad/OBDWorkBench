# Public GitHub release checklist

This package prepares source for review/upload; it does not create a repository, tag, release or public deployment.

## Source gate

- Review README, version/revision, CHANGELOG, LICENSE, third-party notices and reference provenance together.
- Run `python scripts/check-release.py`. Keep bin/obj/publish, temporary WPF projects, IDE files, settings, captures, logs and proprietary research binaries out of the commit/source ZIP.
- Use repository-tracked source when preparing a GitHub source release. Include Branding, docs, ReferenceData and the complete GPSService folder, including its Assets and notices.
- Review external-reference provenance (including the SSM mapping table) before the first public tag; retained documentation is a provenance record, not a legal clearance certificate.

## Windows build gate

- Run `public-release-preflight.bat` on Windows with a clean checkout and .NET 8 SDK.
- Run the full Core suite without --no-ipc so named-pipe and TCP checks execute.
- Build the optional GPS Tray/Service and run its parser/widget tests.
- Run `publish-win-x64.bat`; confirm CSV data, Docs and license/notices accompany the EXE.
- The supplied CI workflow covers build/tests; it has not been run on GitHub during package preparation.

## Manual acceptance gate

- Verify Workbench EXE, splash and taskbar icon at normal/high DPI with both skins; check the splash at restored/maximized/small sizes.
- Verify GPS monitor EXE/taskbar icon and offline/acquiring/fix tray badges. Rebuild/update the installed monitor separately. Unpin/re-pin stale shortcuts if Windows caches an old icon.
- Confirm service diagnostics arrive without a fix, both GPS sources can be selected, settings persist, and only one program owns the physical receiver port.
- Test receiver unplug/reconnect, lost fix, service stop/restart, CSV recording/replay and GPS/ECU trend coexistence.
- Exercise splash family selection, live/calculated values, calibration, gauges, trends, touch navigation and offline help.
- Replay: verify the connection panel starts collapsed, the top toggle opens/closes it, and classic/LCARS skin changes plus recording reloads preserve the current state. Expanded adapter controls must remain read-only.
- On supported hardware, read DTCs and create/review a Support ZIP. Avoid destructive tests on unknown ECU profiles. Independent GEMS vehicle validation remains outstanding.

Record the release machine/OS, exact commit, commands and results in the release notes. Mark unchecked hardware paths as unverified. Source cleanup and compilation do not establish full vehicle compatibility.
