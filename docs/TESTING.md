# Public Compatibility Testing

The goal of public testing is not merely to report "works" or "doesn't work". Workbench is designed to capture enough evidence to identify a new ECU or adapter behavior without requiring the tester to interpret raw bytes.

## Recommended test sequence

1. Start with ignition on, engine off.
2. Connect using the correct splash tile.
3. Note whether the ECU profile/fingerprint is recognized.
4. Perform read-only discovery and DTC reads first.
5. Use **Mark event** for physical actions and visible symptoms.
6. If appropriate, start the engine and repeat the relevant reads.
7. Create a **Support ZIP** before closing Workbench. Open `CaptureStatus.txt` and confirm the session log, CSV recording, live snapshot and event list are either included or explicitly reported unavailable.

## Unknown SSM/TD5/GEMS variants

An unknown fingerprint is valuable. For SSM, the BF System ID, ROM ID, capability bytes, advertised parameter list and A8 behavior are captured in the Support ZIP. Do not try to force it into an existing vehicle profile. The support bundle preserves the response family and identity evidence so a new profile can be added later.

For unknown TD5 profiles, write operations are intentionally restricted. Reads and logging should continue. For SSM, DTC reads may be available when a complete recognized bank layout is advertised, but the engine-memory reset remains disabled until that ECU fingerprint is validated.

## Useful issue information

Include:

- vehicle year/model
- engine/ECU type if known
- ECU label/part number if convenient to read
- adapter make/model
- Workbench version
- what worked and what did not
- Support ZIP attachment

Avoid manually editing the ZIP before reporting unless you need to remove additional personal information.

- Verify Gain + Offset from Live Data (for example gain 1.06), then use **Set zero** on a stable signal and confirm Live Data, Dashboard, Trending and CSV display Value agree while NativeValue/raw data remain unchanged.
- Create a calculated value such as Boost = `A - B` or °F→°C = `(A - 32) * (5 / 9)`; confirm it updates live, can be added to Dashboard/Trending, and appears in the CSV.
- Dashboard: test 2/3/4/5-column grids, two visible gauge rows, decimal places, and low/high colored sweep bands.
- Trending: enable recorded-sample dots, drag a rectangle to zoom twice, then use Reset zoom.
- Touchscreen: tap each top navigation item and verify the content switches on the first touch.
- Preferences → Polling priority: confirm TD5 requests show decoded plain-English signal descriptions and Manifold Absolute Pressure is no longer marked candidate.
