# Subaru SSM2 Connection Guide

## Supported path in this public-test build

Workbench uses native Subaru SSM2 through a direct K-Line serial adapter at **4800 baud, 8N1**. It does not send ELM AT commands on the SSM path.

The validated development interface is an FT232RL-based VAG-COM/KKL-style cable. Select the **SSM** splash tile so Workbench uses the native SSM transport rather than probing the COM port as an ELM adapter.

## First connection

1. Connect the KKL adapter to the vehicle and Windows PC.
2. Turn ignition ON. Engine OFF is preferred for the first connection.
3. Start Workbench and select **SSM**.
4. Select the COM port for the KKL adapter.
5. Choose **Connect + Initialize**.
6. Workbench sends the SSM `BF` initialization request, validates the ECU response/checksum, reads the ECU capability flags, and builds a capability-filtered live-data list.
7. Workbench selects the DTC-bank layout only when the BF identification/capability data matches a known supported layout. A failed/partial bank read is never reported as "no faults".
8. Automatic CSV/session recording starts during initialization. Use **Support ZIP** if the ECU identity, DTC layout or parameter behavior differs from known testing.

## SSM DTC read

The **Refresh DTCs** control performs complete A8 reads of all DTC-bank addresses advertised by the recognized SSM layout. Current/temporary and history/memorized banks are retained separately. Known address/bit combinations are mapped through `ReferenceData/SSM_DTC_MAP.csv`; unknown set bits remain visible with their raw address/bit rather than being discarded.

The current validated development ECU is:

- System ID: `A2100E`
- ROM ID: `2D1A255105`

Other SSM fingerprints may still use DTC read when their BF data selects a recognized bank layout, but write/reset operations remain gated until that ECU profile is validated.

## SSM fault-memory reset

On the validated ECU fingerprint, **Erase DTCs** exposes the guarded engine-memory reset previously validated in the standalone SSM DTC tester.

Before any write Workbench:

1. takes a fresh complete DTC snapshot;
2. verifies that engine-speed capability is advertised;
3. reads RPM (and vehicle speed when advertised) **twice**;
4. requires RPM and vehicle speed to be zero;
5. sends exactly one `B8 00 00 60 40` request;
6. never retries that write merely because the acknowledgement is lost;
7. reconnects, verifies the ECU identity is unchanged, and rereads all selected DTC banks.

The validated transmitted packet is:

`80 10 F0 05 B8 00 00 60 40 DD`

The expected acknowledgement payload is `F8 40`.

**Important:** this operation resets SSM engine fault memory and may also reset learned engine adaptations. It does not claim to clear OBD permanent DTCs. Keep ignition ON, engine OFF and the vehicle stationary.

## What Workbench records

The support data includes:

- full BF initialization data
- SSM System ID and ROM ID
- ECU fingerprint/profile status
- count and list of BF-advertised parameters
- current adaptive A8 batch limit
- DTC profile and bank boundaries when recognized
- latest raw DTC-bank bytes
- transmitted/received SSM frames in the session log
- live-data CSV samples and raw parameter bytes
- current live-value snapshot even if the rolling CSV is unavailable
- persistent user event markers
- explicit capture status for every expected support-bundle evidence file

An unfamiliar SSM fingerprint is useful compatibility data. Do not force it into the known profile.

## Protocol details

Current implementation:

- physical interface: ISO 9141-compatible K-Line
- serial: 4800 baud, 8 data bits, no parity, 1 stop bit
- tester address: `F0`
- engine ECU address: `10`
- initialization service: `BF`
- memory read service: `A8`
- positive A8 response service: `E8`
- initial A8 batch ceiling: 43 unique addresses, with automatic smaller-batch fallback on rejected reads
- guarded engine-memory reset service: `B8 00 00 60 40`, expected response `F8 40`

## Current limitations

Capability support does not prove that a physical sensor is connected or producing a valid value. Raw bytes are preserved so unusual readings can be analyzed later.

SSM fault-memory reset is intentionally write-gated to the already-validated ECU fingerprint. Unknown fingerprints remain useful read/log/support-bundle test targets.
