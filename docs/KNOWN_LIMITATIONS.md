# Known Limitations - v0.5.0.0 Public Test Preview

## SSM

- Native SSM2 live data from v0.4.1.0 is restored: BF capability discovery, capability-filtered A8 reads, logging, replay and adaptive batching.
- SSM DTC-bank reading is exposed only when the BF identification selects a recognized complete layout. The guarded engine-memory reset is write-gated to the validated A2100E / 2D1A255105 fingerprint.
- Capability support does not prove a sensor is physically present or healthy; preserve raw values when reporting unusual data.
- A8 batch limit 43 is an empirical starting point for the tested ECU/interface, not a protocol-wide guarantee.

## TD5

- The validated write/clear profile is based on the early TD5/10P-compatible ECU configuration tested during development.
- Later TD5/NNN variants may return different identifiers, lengths, local-identifier layouts, or fault mappings.
- `Current` or `Logged` bitmap status does not imply that the MIL/check-engine lamp must be on.
- Some bitmap positions remain experimentally associated but not semantically decoded, especially portions of groups 16, 18, 24 and 26.

## GEMS

- Workbench's GEMS dealer-session implementation has been reconstructed from published/recovered utilities but requires independent vehicle validation.
- Cheap ELM327 clones may not implement all required initialization commands reliably.
- GEMS immobilizer synchronization is not exposed.
- GEMS adaptive reset is not exposed in this preview.
- Generic OBD DTC reads may vary with adapter implementation and vehicle market/version.

## General

- Public-test data can reveal new ECU variants that are intentionally treated conservatively until reviewed.
- A successful connection does not prove every decoded signal has the same scaling on every calibration.

## GPS, release and platform status

- GPS source selection requires the Workbench enable checkbox; receiver status without a location fix is supported and has been confirmed on the Windows test PC.
- GPS gauges retain their last sample; trends can join across gaps. Use Fix Valid/status events to distinguish fresh values. No maps, clock synchronization or browser Location Services integration is supplied.
- Direct COM and service mode have different documented freshness rules (three vs five seconds). Missing fields remain unavailable.
- Windows GUI/taskbar/installer and full hardware acceptance remain release-machine checks. Core portability does not mean a Linux/Android/iOS desktop product is included.
- DRB/microPOD is a disabled placeholder in this public package.
- The LCARS skin remains a work in progress.
