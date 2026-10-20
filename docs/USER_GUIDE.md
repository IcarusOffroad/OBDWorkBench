# Workbench user guide

## Connect

Select the ECU family from the splash screen, choose its adapter/COM port or TCP endpoint and connect. OBDII uses ELM/STN protocols; raw SSM and TD5 use their documented K-Line interfaces. Protocol-specific guides describe initialization and supported operations. **Skip** enters the workspace when no vehicle is available. DRB is a disabled preview placeholder.

GPS can connect independently of the vehicle. Open Preferences → GPS connection and NMEA debug, choose a source and check Enable GPS in Workbench. The top indicator opens this screen. See [GPS](GPS.md).

## Live data and units

Switch between rows/cards, filter signals and star readings to pin them. Source labels identify the ECU or GPS that produced a value. A reported ECU value is not proof a sensor is connected or correctly scaled. Preferences sets Imperial/Metric and per-measurement overrides. GPS altitude is metres and coordinates/course are degrees.

Double-click a numeric signal, gauge or configured trend to set gain, offset and decimal places. Display calibration applies after native-to-display unit conversion. Set zero computes an offset from the current reading. Raw/native values remain in recordings.

Right-click a numeric reading to create a calculated value using A/B/C/D arithmetic. A is the source reading; B/C/D are constants. Safe arithmetic supports numbers, parentheses and + - * /. Calculated values have their own name/units and can be pinned, logged, gauged and trended. Calibration/calculated signals affect displayed analysis, not ECU memory.

## Dashboard and trends

Use the numeric reading's context menu to add it to the dashboard or trend. Configure gauge columns, ranges, colors and low/high bands. Configure trend colors, autoscaling/manual limits, sample dots, overlay and time window. Drag a rectangle on the chart to zoom, repeat to zoom farther, and Reset zoom to return. Save named presets for repeat use.

Gauges retain their last value when a source stops; this is not a fresh measurement. GPS cards show unavailable navigation after a lost/stale fix. Historical trend lines may connect samples across gaps; trend GPS Fix Valid or inspect status events to identify those intervals.

## Record, mark events and replay

Automatic recording captures vehicle sessions through the existing recorder. The Auto record control can toggle recording; check the current log path/status rather than assuming a CSV exists. GPS joins the same CSV whenever that recorder is active. GPS service mode retains publication/receiver times in each sample's JSON RawResponse; CSV Timestamp remains Workbench's PC observation time.

Use Mark event to annotate physical actions. Logs are local under `%LOCALAPPDATA%/OBDWorkbench/Logs`; Support ZIPs are under `SupportBundles`. The Logs control opens the log location. Nothing is uploaded automatically.

Select Replay from the splash screen to load a Workbench CSV. Use the replay controls to play/pause, scrub and inspect saved signals. Replay starts with the live connection panel collapsed. The top connection triangle toggles the panel if recorded connection information is needed; its controls are read-only. Switching skins preserves this replay panel state. Replay reads recorded data; it does not open the GPS or command the vehicle. GPS channels keep their source identity across recording/replay.

## DTCs, settings and support

The DTC screen reads supported ECU fault information. Clear operations depend on connection family and validated fingerprint. Unknown profiles remain conservative; consult the family guide. GEMS adaptation/immobilizer/reset writes are not exposed.

Preferences controls units, appearance (classic/Icarus/LCARS) and polling overrides. GPS source/serial settings save immediately, independently of the main Preferences Apply/Cancel flow. Workbench settings live under `%LOCALAPPDATA%/OBDWorkbench`, including `settings.json` and `OBDWorkbenchConfig.json`. Back up that folder before resetting layouts. The independent service stores its own machine settings under `%ProgramData%/Icarus/GPS`.

For a reproducible problem, note the steps and create a [Support ZIP](SUPPORT_BUNDLES.md). Review GPS coordinates and other private information before sharing it publicly.
