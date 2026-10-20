# Portability boundaries — v0.5.0.0

This release prepares the existing Windows application for a future Android host.
It does not contain an Android app, Android drivers, or a completed UI-independent
application controller. The current Windows UI remains the reference implementation.

## Projects and dependency direction

| Project | Owns | Dependencies |
| --- | --- | --- |
| OBDWorkbench.Core (`net8.0`) | PID/DTC decoding and databases, responder/vehicle logic, polling scheduler, TD5 protocol/session/security, ELM protocol, TCP transport, settings models/storage, CSV/session logging, replay indexing/cache, trend time-window state | .NET runtime libraries only |
| OBDWorkbench.Platform.Windows (`net8.0-windows`) | ELM SerialPort access, COM enumeration/registry discovery, native raw K-line break/serial operations | Core, System.IO.Ports |
| OBDWorkbench.Windows (solution display name; existing `OBDWorkbench/OBDWorkbench.csproj` path retained) | WPF windows, UI models/brushes/controls, splash, Windows theme/placement, user dialogs, UI dispatch, current live/replay orchestration and configuration snapshots | Core, Windows platform |
| RegressionTests (`net8.0`) | Portable behavioral tests using fake ELM/K-line transports and a TCP loopback server | Core directly |

Core must not reference either Windows project. The tests reference the same
Core assembly used by the application; they no longer compile private copies
of selected production files.

## Transport contracts

`IObdTransport` represents an already connected ELM byte stream. A platform host
opens the device (including any permission prompts), then passes ownership to
`Elm327Connection.AttachTransport`. Core owns command normalization, input drain,
ELM prompt detection, metrics, timeout handling, monitor start/stop and serialization.

The contract uses a single pull reader (`ReadAvailable`) instead of competing
DataReceived callbacks. It must return immediately if no bytes are buffered.
An Android implementation can buffer native Bluetooth/USB callbacks internally
and expose that buffer through this interface. Writes must complete or fail in
a finite time. `SendCommandDetailedAsync` keeps the blocking compatibility path
off the UI thread and supports cancellation during response waits. Do not call
the synchronous methods from a mobile UI thread. Transport opening and permissions
belong to the host; TCP already has a portable ConnectAsync implementation.

Windows ELM connection uses `WindowsSerialTransport.Open`; TCP uses the shared
`TcpObdTransport`. Their existing baud, framing, drain limit, read cadence, write
timeout and monitor stop sequence have been retained. Physical adapter behavior
still requires a Windows smoke test after this refactor.

`IKLineTransport` is separate. Raw TD5 initialization needs 10400 baud, precise
break pulses, echo handling and timed byte capture. `WindowsKLineTransport`
contains those existing native operations. `Td5KLineConnection` in Core contains
authentication, session recovery, security keys, requests, checksums, and response
parsing. Do not substitute a generic ELM transport for this capability.
An Android raw K-line implementation must prove these timing capabilities on
its USB driver/interface before it can be considered supported.

## Storage and logging

`AppSettingsStore` receives an explicit settings file path.
`ReplayCsvLoader.Load` receives an explicit cache root directory.
Core does not choose LocalApplicationData or access Registry/Windows handles.
The Windows host supplies exactly the prior settings and replay-cache locations
through `WindowsStoragePaths`. Existing JSON/CSV/cache schemas are unchanged.

CSV and session file writers remain shared .NET code and accept caller-provided
folders. `ISessionDiagnosticLogger` permits a different diagnostics sink; the
Windows MainWindow accepts an optional injected implementation. Android can use
private app directories, then handle document picking/export/sharing in its host.
Android content URIs must be copied/opened through the platform APIs; they are
not ordinary local paths understood by the shared replay file loader.

## Rules for future changes

- Add protocol decoding, calculations, vehicle rules, polling algorithms and
  reusable file formats to Core, with portable regression checks.
- Keep WPF Dispatcher, MessageBox, Brush, Window, file dialogs and Registry out of Core.
- Keep physical USB/Bluetooth/serial driver details in platform implementations.
- Keep signal values numeric and source-aware in shared models; presentation
  colors, chart controls and localized UI text belong in the host.
- Add future SSM/DRB providers behind appropriate transport boundaries. Do not
  introduce Windows-native calls into their shared protocol implementations.
- Preserve cancellation/timeout semantics and single-reader transport ownership.
- Run the portable suite and the Windows build before packaging a new version.

## Remaining work before an Android app

The current MainWindow still coordinates discovery, live polling, display updates,
DTC refresh and replay playback. The reusable parsers/schedulers/cache are now in
Core, but extracting that orchestration into a shared session controller is a
separate next step. WPF view models currently containing brushes, gauge/trend
rendering state and dispatcher timers remain Windows-specific.

An Android host will need its own screens, lifecycle management, permissions,
Bluetooth/USB transports and document import/export. Wi-Fi/TCP can reuse the
existing shared transport. The project structure does not force a choice between
a .NET MAUI host and a .NET for Android host. No mobile workload is needed to keep
developing or testing the portable Core today.


## SSM (restored in v0.5 public-test r3)

Core owns `SsmConnection`, `SsmProtocol`, `SsmParameterCatalog` and `ISsmTransport`. `WindowsSsmTransport` retains the tested Win32 serial implementation from the v0.4.1.0 branch. `MainWindow.Ssm` adapts capability-filtered readings into the existing live values, trends, dashboards, CSV logger, ECU fingerprinting and support-bundle workflow. Only BF initialization and A8 reads are exposed through the Workbench UI in this build.

## GPS and optional Windows service

Direct NMEA COM input uses WindowsGpsConnection plus the shared NmeaGpsParser. Service mode uses Core's read-only GpsServiceFeed against the Icarus.GPS.Data.v1 named pipe and validates protocol/publication freshness with GpsServiceValues. The WPF dispatcher consumes snapshots and routes values to the same GPS live/trend/gauge/CSV identities. Source/status changes are recording events. Service publication UTC and receiver UTC remain in JSON sample evidence. Neither source participates in ECU polling.

The independently buildable GPSService folder includes Service, Tray/desktop widget, Core snapshot/parser types and parser/widget tests. Configuration/control uses an administrator-protected pipe separate from the read-only subscriber. Workbench never automatically changes machine service settings or hands off COM ownership. Integration/IcarusGpsProtocol.cs in Workbench Core is retained from that version-1 schema; changes must keep both contracts compatible.

Public docs live in docs/ and are copied to the runtime Docs/ directory. Branding contains editable icon masters; embedded consumer ICO copies are checked by scripts/check-release.py. Prebuilt icon assets keep normal .NET builds independent of graphics tooling. Generated build artifacts and live captures are excluded from source releases.
