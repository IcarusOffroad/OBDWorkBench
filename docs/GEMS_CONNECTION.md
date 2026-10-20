# Land Rover GEMS Connection Guide

GEMS support in v0.5.0.0 is a **public-test preview**. The dealer-session implementation was reconstructed from RobertF/FlemcoDesign GEMS utilities and public protocol documentation, but this Workbench implementation still needs broad vehicle testing.

## Hardware

Use an ELM327/STN-compatible adapter connected to the vehicle's 16-pin diagnostic connector. OBDLink/STN hardware is preferred for testing. Generic ELM clones may omit or incorrectly implement commands required by the dealer session.

## Workbench steps

1. Connect the ELM/STN adapter.
2. Turn ignition on.
3. Start Workbench and choose **GEMS**.
4. Select the serial COM port or configured Wi-Fi/TCP endpoint.
5. Click **Connect + Initialize**.

Workbench configures the recovered dealer path:

- `ATE0`
- `ATS0`
- `ATAL`
- `ATH0`
- `ATSP4` - ISO 14230-4 / 5-baud init
- `ATIIADA` - initialization address `DA`
- `ATSHC133F1`
- `ATSI`
- diagnostic session `10 02`
- SecurityAccess `27 01`
- key = `(seed * 0x4153) & 0xFFFF`
- `27 02 <key>`

Known dealer reads collected by this preview include `22 23 32`, `22 23 33`, `22 23 34`, `22 23 36`, `22 23 37`, `22 04 BF`, `22 04 C4`, and `22 04 E2`.

The first three are labeled from the recovered RobertF Windows utility as air adaptive value, fuel adaptive value, and learned closed-throttle value. The remaining values stay conservatively labeled until vehicle testing confirms their semantics.

## DTCs and writes

**Refresh DTCs** temporarily uses the standard generic ISO 9141 OBD session and then restores the GEMS dealer session.

GEMS DTC clearing, adaptive reset, and immobilizer synchronization are intentionally not enabled in this public-test preview. The known commands are retained in project reference material but should not be exposed as public write operations until the new implementation is validated on real vehicles.
