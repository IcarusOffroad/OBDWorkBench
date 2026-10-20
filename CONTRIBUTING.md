# Contributing

This preview welcomes reproducible adapter/vehicle tests and focused fixes. Start with [building](docs/BUILDING.md), [architecture](ARCHITECTURE.md) and [testing](docs/TESTING.md).

## Report a problem

Include the source revision, Windows version, selected connection family, vehicle year/engine/ECU identity where known, adapter model/transport and exact steps. For GPS include selected source, enabled checkbox state, service version/status and whether NMEA arrives without a fix. Describe expected and actual behavior. An exact build error is more useful than a cropped screenshot.

Use **Mark event** during a vehicle experiment, then **Support ZIP**. Review the ZIP before attaching it to an issue. VIN/user-path sanitization does not remove GPS coordinates, route history or text you entered. Do not upload credentials, private firmware, proprietary diagnostic databases or third-party utility binaries.

## Submit a change

Keep changes scoped, explain behavior and validation, and preserve source/provenance notes. Test shared-Core behavior with `test-core.bat` and build the Windows solution. GPS monitor changes also need its parser/widget tests and Tray/Service builds. Run `python scripts/check-release.py` for source/documentation checks. Tests that use Windows named pipes should remain enabled on the Windows release machine.

Prefer injected fake transports for protocol tests. Do not remove ECU-fingerprint write gates or issue guessed actuator/clear commands to unknown ECUs. Document observed data separately from assumed scaling. Keep Core independent of WPF and Windows serial dependencies.

For a pull request summarize the problem, change, tests and any receiver/vehicle checks still unperformed. New contributions must be compatible with the project license and retain attribution where required. No CLA process or automatic publication is configured here.
