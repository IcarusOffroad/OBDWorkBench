# OBD Workbench Protocol Reference - Public-Test Snapshot

This document records the protocol facts currently implemented so future development is not dependent on external websites remaining online.

## Subaru SSM2 native K-Line

Restored from the tested v0.4.1.0 Workbench branch:

- 4800 baud, 8N1 native serial K-Line
- tester address `F0`, engine ECU address `10`
- packet form: `80 DEST SRC LEN DATA... CHECKSUM`
- additive low-byte checksum over all preceding packet bytes
- BF initialization request: `80 10 F0 01 BF 40`
- capability flags in the BF response determine which catalog parameters are eligible to poll
- A8 memory-address read service, positive response service `E8`
- initial Workbench A8 batch ceiling 43 unique addresses, with automatic smaller-batch fallback
- full BF raw response, System ID, ROM ID, capability set and advertised parameter list are retained in public-test support data

The validated development ECU identification recorded in project testing is System ID `A2100E` and ROM ID `2D1A255105`. Workbench fingerprints the System ID, ROM ID and BF capability bytes so a different Subaru ECU is preserved as a distinct profile rather than assumed compatible.

SSM DTC-bank and clear-command research is retained in `COMMUNICATIONS_REFERENCE.md` and `ReferenceData/SSM_DTC_MAP.csv`, but those write/DTC controls are not exposed by the v0.5 public-test Workbench UI.

## TD5 native K-Line

Validated development sequence:

- 10400 baud, 8N1 after fast initialization
- ~25 ms K-line low, ~25 ms high
- StartCommunication: `81 13 F7 81 0C`
- positive response observed: `03 C1 57 8F AA`
- StartDiagnosticSession: `02 10 A0 B2`
- SecurityAccess seed: `02 27 01 2A`
- SecurityAccess key uses the TD5-derived LFSR algorithm implemented in `Td5KLineConnection`
- positive key response observed: `02 67 02 6B`
- local identifier reads: `[02 21 PID checksum]`
- fault bitmap: PID `3B`, exactly 35 bitmap bytes
- clear-fault routine: `14 31 DD` + eighteen zero bytes + additive checksum (`22` in the validated frame)
- positive clear response observed: `02 71 DD 50`

### Development ECU static identity

The validated early profile returned:

- PID 0E ASCII: `ssttttllpp001100`
- PID 32 ASCII: `sttlp010sthle0224143`
- PID 33 ASCII: `TPUM0101thbtp003`

Workbench hashes these values into a stable profile fingerprint. Different values are intentionally treated as a new ECU flavor rather than silently assumed compatible.

## GEMS dealer session

Recovered from the RobertF/FlemcoDesign GEMS Windows and Android utilities and cross-checked against ELM/STN command behavior:

Adapter/session setup used by the Android implementation:

- `ATE0`
- `ATS0`
- `ATAL`
- `ATH0`
- `ATSP4`
- `ATIIADA`
- `ATSHC133F1`
- `ATSI`

Application/session messages:

- StartDiagnosticSession: `10 02` -> positive `50 02`
- SecurityAccess seed: `27 01` -> `67 01 <seed>`
- key algorithm: `(seed * 0x4153) & 0xFFFF`
- send key: `27 02 <key>` -> positive `67 02 ...`

Known reads present in the recovered applications:

- `22 23 32` - air adaptive value
- `22 23 33` - fuel adaptive value
- `22 23 34` - learned closed-throttle value
- `22 23 36`
- `22 23 37`
- `22 04 BF`
- `22 04 C4`
- `22 04 E2`

Generic GEMS DTC reads temporarily restore ISO 9141 init address `33` and header `68 6A F1`, then restore the DA/C1 33 F1 dealer session afterward.

Known but intentionally disabled public-test writes:

- `A3 23 48 00` - reset adaptive values
- `A3 00 62 25 88` - immobilizer/alarm synchronization

The older Windows GEMS utility used explicit length + payload + additive checksum framing with STN-specific checksum controls; for example `02 10 02 14` and `02 27 01 2A`.

## External references retained by project history

- LibSSM2 / RomRaider / FreeSSM factual SSM protocol and parameter references retained in project provenance.
- RobertF/FlemcoDesign GEMS utility binaries and Android APK retained with the project research material.
- EA2EGA/Ekaitza_Itzali TD5 diagnostic project.
- pajacobson/td5keygen TD5 SecurityAccess work.
- JRogers83/TD5-Dash TD5 protocol notes.
- TD5SPY fault-list/reference material.
- ELM Electronics ELM327 documentation.
- ScanTool/STN command documentation.

When external references disagree with controlled vehicle experiments, Workbench keeps the published label and the experimental observation separate rather than silently rewriting one into the other.
