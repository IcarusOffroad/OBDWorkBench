# Land Rover TD5 Connection Guide

## Hardware

- FT232RL-based KKL/VAG-COM style K-Line adapter recommended.
- Vehicle DLC K-line is on OBD-II pin 7.
- OBD-II pin 16 supplies vehicle battery power to adapters that require it.
- OBD-II pins 4 and 5 are grounds.

Do not connect external voltage to the K-line.

## Workbench steps

1. Connect the KKL adapter to the vehicle and PC.
2. Turn ignition on.
3. Start Workbench and choose **TD5**.
4. Select the KKL COM port.
5. Click **Connect + Initialize**.

The validated early-TD5 path performs:

- approximately 25 ms fast-init low/high timing
- StartCommunication `81 13 F7 81 0C`
- diagnostic session `02 10 A0 B2`
- SecurityAccess seed request `02 27 01 2A`
- TD5 seed/key calculation
- Service `0x21` local-identifier reads

## ECU profile / fingerprint

Workbench records static TD5 identifiers and generates a fingerprint. The development Defender's validated early/10P-compatible configuration is recognized explicitly. A different fingerprint remains usable for read/logging, but write operations such as fault clearing are disabled until that ECU flavor is validated.

## Fault memory

The TD5 `0x3B` response is treated as exactly 35 bitmap bytes (280 positions). Current/logged diagnostic flags do not necessarily request the MIL.

For the validated fingerprint, **Erase DTCs** uses the one-shot `31/DD` clear routine and automatically captures pre/post-clear snapshots. Workbench never automatically retries that write if the acknowledgement is lost.
