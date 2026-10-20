# OBDWorkbench Communications Reference

**Canonical project reference for diagnostic communications, protocol behavior, evidence, and source provenance**

Last updated: 2026-09-24  
Document status: living reference  
Baseline application inspected: OBDWorkbench v0.4.1.0  
Scope: standard OBD-II/ELM327, Subaru SSM2, Land Rover TD5, Chrysler/Jeep DRB III and SCI/microPOD research, Land Rover GEMS research, and cross-protocol transport/parsing rules.

---

## 1. Purpose

This document is intended to be the single human-readable source of truth for what the OBDWorkbench project currently knows about vehicle diagnostic communications.

The source code remains authoritative for the exact behavior of a released build, but source code alone is a poor long-term notebook. Important protocol discoveries have come from standards summaries, vendor documentation, open-source diagnostic projects, reverse engineering, packet captures, binary inspection, and direct experiments on real ECUs. This file records the useful technical facts from those sources in plain English, together with their provenance and confidence level.

A core goal is **survivability of knowledge**. A web page, forum thread, repository, or download may disappear. Therefore this document does not merely point at a URL. For each important external source it records:

1. what the source contributed;
2. the relevant technical facts, rewritten here in project language;
3. the original URL and access date;
4. a stable revision or commit when one is available;
5. whether the information has also been confirmed by OBDWorkbench/project testing.

This is not intended to be a mirror of copyrighted manuals or web pages. We preserve protocol facts, observations, derived explanations, small command examples, and our own summaries. We do not reproduce entire copyrighted documents or substantial passages verbatim.

---

## 2. Evidence labels

Every protocol fact should be mentally tagged with one or more of the following levels. New findings should keep these labels rather than silently graduating from a guess into a "fact."

| Label | Meaning |
|---|---|
| **[STD]** | Defined by a formal standard, vendor documentation, or a directly published command reference. |
| **[SRC]** | Found in a public reference implementation, reverse-engineering project, database, or source code. |
| **[OBS]** | Directly observed by this project in a capture, ECU response, scanner session, test utility, or Workbench log. |
| **[INF]** | Strongly inferred from multiple observations or binary/code inspection, but not yet independently demonstrated end-to-end. |
| **[EXP]** | Experimental candidate mapping or interpretation that is useful enough to retain but needs more testing. |
| **[UNK]** | Unknown or intentionally unresolved. Raw bytes should be retained so the question can be revisited later. |

A statement can have multiple labels. For example, the TD5 seed/key process is **[SRC][OBS]** because public implementations describe it and our vehicle tests have successfully completed it.

---

## 3. The communication stack: what layer are we actually talking to?

A recurring source of confusion in automotive diagnostics is using the word "protocol" for several different layers. OBDWorkbench separates them conceptually:

```text
OBDWorkbench application
        |
        +-- transport API
        |      Serial COM / USB-serial
        |      TCP socket / Wi-Fi ELM
        |      J2534 / vendor VCI API (research path)
        |
        +-- adapter or interface
        |      ELM327/STN style interpreter
        |      KKL USB serial transceiver
        |      microPOD / VCI
        |
        +-- physical vehicle bus
        |      K-Line / ISO 9141 electrical layer
        |      SCI
        |      J1850
        |      CAN
        |      CCD/PCI, etc.
        |
        +-- diagnostic framing/session
        |      SAE OBD-II
        |      KWP2000-like session
        |      Subaru SSM2
        |      Land Rover TD5 native service scheme
        |      Chrysler SCI/DRB command set
        |      Land Rover GEMS dealer session
        |
        +-- application data
               PID / memory address / signal / DTC / actuator / coding
```

Two interfaces can use the same physical K-Line wire while speaking completely different diagnostic languages. Conversely, an ELM327 may hide much of the physical initialization and checksum work, while a raw KKL interface exposes it to the application.

This is why OBDWorkbench deliberately maintains separate transport abstractions for ELM-style command interpreters and timing-sensitive native K-Line communications.

---

# Part I: ELM327 and standard OBD-II

## 4. ELM327 role

### 4.1 What the ELM is

**[STD][OBS]** The ELM327 is an interpreter between an ASCII command channel and one of several vehicle diagnostic buses. The host sends two broad command classes:

- `AT...` commands configure the ELM itself.
- hexadecimal OBD/vehicle requests are transmitted toward the vehicle using the selected vehicle protocol.

The host normally terminates commands with carriage return. The ELM normally terminates its response with a `>` prompt. OBDWorkbench uses the prompt as the primary command-completion boundary.

### 4.2 OBDWorkbench ELM transport behavior

**[OBS]** Current `Elm327Connection` behavior:

- Can attach to an already-open serial/USB style transport or a TCP transport.
- Normalizes an outgoing command by removing embedded CR/LF and appending one `\r`.
- Drains stale input before issuing a command.
- Reads until `>` appears or the command timeout expires.
- Records first-byte time and total response time. Tail time is total minus first-byte time.
- Has a monitor mode using `ATMA`; monitor mode is stopped by sending a space plus carriage return.
- Can drain the input until a quiet interval is observed, which is useful after odd multi-frame or clone behavior.

### 4.3 Typical initialization used by Workbench

A commonly observed startup sequence is:

```text
ATZ        reset adapter
ATE0       echo off
ATL0       linefeeds off
ATH1       headers on
ATSPx      select a known protocol when appropriate
0100       test OBD response / supported PIDs
ATDP       ask adapter to describe selected protocol
```

When the protocol is not known, Workbench can allow ELM automatic protocol search with `ATSP0`. When a previously confirmed protocol is known, it can try the saved protocol first to avoid repeated searching.

### 4.4 ELM command facts retained from the ELM Electronics reference

**[STD]** Important commands used or relevant to this project include:

| Command | Meaning retained for this project |
|---|---|
| `ATZ` | reset the interpreter |
| `ATE0` / `ATE1` | command echo off/on |
| `ATL0` / `ATL1` | linefeeds off/on |
| `ATH0` / `ATH1` | vehicle headers off/on |
| `ATDP` | describe the currently selected vehicle protocol |
| `ATDPN` | describe selected protocol by number |
| `ATSPx` | select protocol number `x`; `0` means automatic search |
| `ATSH...` | set vehicle header used for subsequent messages |
| `ATIIA hh` | set ISO slow-init address to `hh` |
| `ATIB10` | set ISO baud rate to 10400 |
| `ATIB48` | set ISO baud rate to 4800 on ELM versions that support it |
| `ATSI` | perform ISO slow initialization |
| `ATFI` | perform ISO fast initialization on versions that support it |
| `ATMA` | monitor all messages |
| `ATST hh` | set timeout value |

The project does **not** assume every device labeled "ELM327" implements every command correctly. Clone firmware varies substantially.

### 4.5 Transport examples confirmed by the project

**[OBS]** Workbench has successfully used:

- serial ELM327-style adapters;
- a Wi-Fi/TCP ELM at `192.168.0.10:35000`;
- adapter identities reporting `ELM327 v1.5` and `ELM327 v2.1`.

The Wi-Fi adapter may have much larger command tail latency than a USB serial unit, even when the first byte appears quickly. First-byte and tail timing therefore need to be treated separately.

---

## 5. Standard OBD-II services used by Workbench

### 5.1 Request and positive-response convention

**[STD]** For the classic OBD services relevant here, a positive response uses the request service plus `0x40`. Examples:

| Request | Positive response | Use |
|---|---|---|
| `01` | `41` | current/live emission-related data |
| `03` | `43` | stored diagnostic trouble codes |
| `04` | `44` | clear emission-related diagnostic information |
| `09` | `49` | vehicle information such as VIN/CALID/CVN |

This convention is valuable to parsers, but response length and transport framing still vary by bus.

### 5.2 Mode 01 supported-PID discovery

**[STD][OBS]** PID `00` is a 32-bit supported-PID bitmap for PIDs `01` through `20`. Additional capability blocks are exposed at `20`, `40`, and so on. Workbench decodes these per responding ECU rather than merging them into one anonymous vehicle-wide list.

A set bit is interpreted MSB-first within each capability byte. Keeping support maps per responder matters because the PCM and transmission controller may advertise different PID sets.

### 5.3 Mode 01 common conversions retained

These are standard examples useful for sanity checks:

| PID | Signal | Raw conversion |
|---|---|---|
| `05` | coolant temperature | `A - 40` °C |
| `0C` | engine RPM | `((A*256)+B) / 4` RPM |
| `0D` | vehicle speed | `A` km/h |
| `0F` | intake air temperature | `A - 40` °C |
| `10` | mass airflow | `((A*256)+B) / 100` g/s |
| `11` | throttle position | `A * 100 / 255` % |

Workbench maintains a larger PID database; this table is retained here as a decoding sanity reference, not as the complete database.

### 5.4 Mode 03 DTC decoding

**[STD][OBS]** Two DTC bytes encode a letter family plus four hexadecimal digits. Workbench applies the conventional P/C/B/U mapping and preserves ECU source attribution.

For legacy non-ISO-TP frames, decoding starts immediately after response service `43`. For reassembled ISO-TP data, the parser accounts for the payload layout produced by its ISO-TP reassembly path.

### 5.5 Mode 04 acknowledgement

**[OBS]** Some replies to clear-DTC service 04 consist only of response service `44`. The parser therefore accepts a one-byte service acknowledgement instead of requiring a PID after every response service.

A subtle project lesson: a `44` acknowledgement from one module does not prove another module has cleared its DTCs. Clear results must retain ECU source.

### 5.6 Mode 09 VIN/CALID/CVN

**[STD][OBS]** Workbench handles both CAN/ISO-TP and legacy Mode 09 layouts.

Important current parser rules:

- VIN uses Mode 09 PID `02`.
- CALID uses PID `04`.
- CVN uses PID `06`.
- ISO-TP messages are reassembled before the application field is decoded.
- Legacy Mode 09 can arrive as numbered frames carrying four data bytes per frame.
- CALID can contain NUL padding. Workbench stops each text item at its first NUL instead of treating padding bytes as text.
- Results are stored per ECU source.

---

## 6. Legacy ISO 9141 / J1850 / KWP parsing lessons

### 6.1 Source address matters

**[OBS]** On older vehicles with three-byte legacy headers, Workbench uses the third header byte as the responding source address. Examples seen in project vehicles include Jeep source `D1` for the PCM and `18` for the transmission controller.

A response should therefore be thought of as:

```text
transport/header + source ECU + OBD payload + transport checksum
```

not merely "the vehicle replied."

### 6.2 ISO 9141 example

**[OBS]** A project Subaru/ELM session produced a classic ISO 9141 response similar to:

```text
48 6B E1 41 00 BF BE A8 11 0B
```

Interpreted at a high level:

- `48 6B E1` is the three-byte legacy header, with `E1` as source.
- `41 00 ...` is the positive Mode 01 PID 00 response and capability data.
- final byte is the transport checksum.

### 6.3 Cheap-clone formatting failures

**[OBS]** Some inexpensive ELM-compatible devices output one hexadecimal byte per CR/LF. Normal line-oriented parsers then see lines too short to be valid vehicle frames.

Workbench has a constrained fallback that:

1. strips whitespace into a byte stream;
2. searches only for known legacy header patterns;
3. reconstructs framed payloads;
4. does not turn arbitrary adapter text into fake ECU messages.

Another observed clone oddity is `BUS INIT: OK` being glued directly to the first response byte. The parser explicitly separates `OK48`-style boundaries before tokenizing.

### 6.4 CAN and ISO-TP

**[STD][OBS]** For CAN OBD responses, Workbench recognizes standard single-frame data and ISO-TP first/consecutive frames. Multi-frame state is tracked per source ECU and consecutive-frame sequence numbers are checked before data is accepted.

### 6.5 Response-count optimization

**[OBS]** Some ELM workflows permit a requested response count to be appended to a vehicle query. Workbench treats response-count use as an optimization, not as a correctness requirement.

Current design rule: do not enable source-count restriction until unrestricted requests have established which responders actually answer. If response provenance becomes uncertain, fall back to unrestricted queries and re-learn the safe count. This prevents a faster query from silently hiding a module.

---

# Part II: Subaru Select Monitor 2 (SSM2)

## 7. SSM2 physical transport

**[SRC][OBS]** The SSM2 protocol used by this project runs on an ISO 9141-compatible K-Line electrical interface at:

```text
4800 baud
8 data bits
no parity
1 stop bit
```

Workbench uses **native serial/K-Line transport**, not ELM ASCII commands, for its SSM path.

This distinction is important. An ELM can support a 4800 ISO baud setting on some firmware, but our tested SSM implementation talks directly through a KKL-style serial interface so packet timing, echo filtering, framing, and checksums remain under application control.

---

## 8. SSM2 packet format

**[SRC][OBS]** The packet structure used by Workbench is:

```text
80  DEST  SOURCE  LENGTH  DATA...  CHECKSUM
```

Known addresses used here:

```text
0x10 = Subaru engine ECU
0xF0 = diagnostic tester
```

`LENGTH` is the number of data bytes.

The checksum is the low eight bits of the additive sum of every preceding byte in the packet, including the `0x80` header.

### 8.1 BF initialization request

Tester request:

```text
80 10 F0 01 BF 40
```

This is a one-byte data payload containing service `BF`; `40` is the additive checksum.

The ECU replies to tester address F0 from ECU address 10 and returns identification plus capability flag bytes. The exact capability length varies by ECU generation.

### 8.2 Project-observed BF response

**[OBS]** The Subaru engine ECU used in September 2026 testing repeatedly returned:

```text
80 F0 10 39
FF A2 10 0E 2D 1A 25 51 05
FF EA AF A2 22 45 03 FE 00 18 00 20 CE 54 FB F0 94 80 00 E0 00 00 00 00 00 DC 00 00 45 1C 00 00 00 00 00 00 00 00 00 80 00 00 00 00 00 00 00 00
D2
```

The probe interpreted the identification as:

```text
System ID: A2100E
ROM ID:    2D1A255105
DTC format family for this ECU: OBD-style SSM bank layout
```

This exact response is valuable as a regression fixture because it has been seen on real hardware.

---

## 9. BF capability flags

### 9.1 Flag numbering convention

**[SRC]** In the current project convention for a BF positive response:

- returned data byte 0 is `FF`;
- the next three bytes contain the system ID;
- the next five bytes contain the ROM ID;
- **flag byte 1 begins at returned data index 9**;
- flag bits are numbered 1 through 8, with bit 1 being the least significant bit.

This convention is mirrored in the Workbench SSM catalog and the standalone DTC probe.

### 9.2 Capability-driven discovery

**[SRC][OBS]** A major rule of the SSM implementation is: do not assume a parameter is supported merely because an address exists in a public table.

The BF flags determine which measuring blocks, switches, and other groups the ECU advertises. Workbench builds its polling candidates from advertised capabilities, then validates real A8 responses.

The September test ECU advertised approximately 65 catalog items. An example probe decode was:

```text
supported = 65
fast      = 17
slow      = 16
switch    = 27
snapshot  = 5
```

Counts may vary slightly as catalog classification evolves. The critical fact is that support comes from the ECU's BF flags, not from a vehicle-model guess.

---

## 10. SSM2 A8 memory-address reads

**[SRC][OBS]** Service `A8` is used by the current Workbench implementation to request selected 24-bit addresses.

Conceptual request data:

```text
A8 00 [address-high address-mid address-low] ...
```

The positive response service is `E8`.

For each requested address the current response layout supplies one returned data byte. Multi-byte logical parameters are represented by neighboring addresses and combined by the parameter definition.

### 10.1 Common parameter addresses retained

These are the core SSM base definitions currently useful to this project. Formulas follow the retained RomRaider/FreeSSM factual definitions unless marked otherwise.

| Signal | Address(es) | Storage | Conversion |
|---|---:|---|---|
| Engine load | `000007` | U8 | `raw * 100 / 255` % |
| Coolant temperature | `000008` | U8 | `raw - 40` °C |
| Manifold absolute pressure | `00000D` | U8 | reference tables commonly treat raw as kPa; Workbench display conversion should be checked against its catalog version |
| Engine speed | `00000E`,`00000F` | U16 BE | value / 4 RPM |
| Vehicle speed | `000010` | U8 | raw km/h |
| Ignition timing | `000011` | U8 | `(raw - 128) / 2` deg |
| Intake air temperature | `000012` | U8 | `raw - 40` °C |
| Mass airflow | `000013`,`000014` | U16 BE | value / 100 g/s |
| Throttle opening angle | `000015` | U8 | `raw * 100 / 255` % |
| Battery voltage | `00001C` | U8 | `raw * 2 / 25` V, equivalent to `raw * 0.08` V |
| Throttle sensor voltage | `00001E` | U8 | `raw / 50` V |
| Atmospheric pressure | `000023` | U8 | raw kPa in FreeSSM base definition |
| Manifold relative pressure | `000024` | U8 | `(raw - 128) * 37 / 255` PSI in RomRaider base definition |
| Fuel temperature | `00002A` | U8 | `raw - 40` °C |
| Fuel pump duty | `00003B` | U8 | `raw * 100 / 255` % |

**Conversion caution:** RomRaider, FreeSSM, and historical SSM tables sometimes present equivalent pressure values using different units or slightly different display conventions. Preserve raw data and source formula. Do not "correct" a formula solely because another implementation displays a different unit.

### 10.2 Test ECU support observations

**[OBS]** One capability-filtered test showed support for RPM, MAP, coolant, speed, IAT, throttle, battery voltage, throttle sensor voltage, atmospheric pressure, and relative MAP. It did **not** advertise MAF on this engine, matching the physical configuration of the tested swap engine.

### 10.3 A8 batching

**[OBS]** A8 requests can contain many addresses, but a tested ECU/transport combination did not accept unlimited request size.

Current project history:

- early standalone SSM probing used conservative batches around 18 addresses;
- later systematic testing established that 43 requested addresses worked while 44 failed on the tested ECU/path;
- OBDWorkbench v0.4.1.0 therefore begins with a 43-address batch ceiling and can split/shrink batches when a request fails.

The number **43 is an empirical implementation ceiling for our tested path, not an SSM2 specification limit**. Other ECUs/interfaces may differ.

A completed multi-batch snapshot is sequential, not atomic. Values at the beginning and end of a snapshot were not captured at exactly the same instant.

---

## 11. SSM echo and response validation

**[OBS]** Native K-Line interfaces may echo transmitted bytes. SSM parsing therefore validates direction:

- tester request: destination `10`, source `F0`;
- ECU response: destination `F0`, source `10`.

A valid packet also needs:

- correct `0x80` header;
- internally consistent length;
- valid additive checksum;
- expected response service (`E8`, `F8`, etc.);
- expected source and destination.

An echoed request is not a valid ECU response simply because its checksum is correct.

---

## 12. SSM DTC storage and decoding

### 12.1 Why SSM DTCs are not just generic Mode 03

**[SRC][OBS]** Subaru SSM fault state can be represented as capability-selected memory banks where individual bits correspond to diagnostic identifiers. The project labels two sets as:

- `Current / temporary`
- `History / memorized`

These labels are deliberately descriptive. They should **not** automatically be equated with generic OBD-II "pending," "stored," or "permanent" categories.

### 12.2 DTC family selection

The standalone DTC probe currently restricts itself to recognized Ax10xx SSM engine identifications and sufficient BF capability data. It refuses to declare "zero faults" if the bank layout is unknown.

**[SRC]** Current bank selection rules retained from FreeSSM-derived factual mappings:

#### Subaru-format base layout

If BF flag 30 bit 8 selects the Subaru-format family:

```text
Base current start:  0x00008E
Base history start:  0x0000A4
Length:              11 bytes each
```

#### OBD-style base and extensions

For the OBD-style family:

| Capability condition | Current start | History start | Length |
|---|---:|---:|---:|
| flag 30 bit 5 or bit 7 | `00008E` | `0000AE` | 32 |
| flag 29 bit 1 | `0000F0` | `0000F4` | 4 |
| flag 40 bit 8 | `000123` | `00012B` | 8 |
| flag 40 bit 7 | `000150` | `000155` | 5 |
| flag 40 bit 6 | `000160` | `000165` | 5 |
| flag 40 bit 5 | `000174` | `00017B` | 7 |
| flag 51 bit 7 | `0001C1` | `0001C7` | 6 |
| flag 51 bit 7 | `00020A` | `00020E` | 4 |
| flag 51 bit 6 | `000263` | `000268` | 5 |

The project-generated `ReferenceData/SSM_DTC_MAP.csv` preserves 616 factual format/address/bit/code mappings derived from the pinned FreeSSM revision documented below. It intentionally does not copy FreeSSM's descriptive prose.

### 12.3 Project-observed Subaru faults

**[OBS]** During the September 2026 test sequence, the SSM decoder agreed with a known scanner on at least:

```text
P0335  crankshaft position sensor circuit
P0183  fuel temperature sensor circuit high
```

The project also directly observed current/history bit changes across ignition and clear tests, demonstrating that the bank distinction is operationally meaningful even though its exact relationship to SAE status categories should not be assumed.

---

## 13. SSM memory clear / DTC clear

### 13.1 Command structure

**[SRC][OBS]** The tested engine-memory clear uses an SSM single-address write:

```text
B8 write
Address: 0x000060
Value:   0x40
```

Complete tested request packet:

```text
80 10 F0 05 B8 00 00 60 40 DD
```

Expected positive acknowledgement data:

```text
F8 40
```

Complete response seen on the test ECU:

```text
80 F0 10 02 F8 40 BA
```

### 13.2 Safety logic retained from the test probe

The DTC test utility deliberately:

- requires ignition on;
- requires engine RPM to read zero twice;
- checks vehicle speed when the ECU advertises it;
- sends exactly one clear write per user action;
- does not automatically retry a lost write acknowledgement;
- reconnects and rereads the ECU afterward;
- warns that clearing engine memory may also reset adaptations/learned values.

The acknowledgement proves the ECU accepted the write request. It does not by itself prove which faults will remain gone after the underlying circuit is evaluated again.

### 13.3 Persistent clear experiment with fuel-temperature simulation

**[OBS]** On 2026-09-23, a resistor was installed to give the fuel-temperature circuit a plausible value around 73 °F. In the successful session:

- the ECU acknowledged the `B8 000060 = 40` clear request with `F8 40`;
- P0183 disappeared after the clear/key-cycle sequence;
- subsequent verification reads continued to show P0335 but not P0183;
- a later session also showed no P0183.

This is important because it demonstrates that the command is capable of a persistent ECU memory clear when the underlying fault is no longer present. Earlier tests with the fault still electrically present showed the fault remaining or returning, which is expected behavior.

This observation should not be generalized into a claim that the SSM memory command erases every emissions-related "permanent DTC" concept used by later standardized OBD systems.

---

# Part III: Land Rover TD5 native K-Line

## 14. TD5 transport

**[SRC][OBS]** The tested TD5 engine ECU path uses native K-Line at:

```text
10400 baud
8 data bits
no parity
1 stop bit
```

Workbench does not send ELM commands on this path. The raw serial transport controls the K-Line initialization waveform and receives its own transmit echo plus ECU response bytes.

---

## 15. TD5 fast initialization and session establishment

### 15.1 Fast-init pulse

**[SRC][OBS]** Current Workbench transport uses an approximately:

```text
K-Line LOW  25 ms
K-Line HIGH 25 ms
```

fast-init transition before the StartCommunication frame.

### 15.2 StartCommunication

**[SRC][OBS]** Tested request:

```text
81 13 F7 81 0C
```

A classic positive response repeatedly observed on the project TD5 ECU is:

```text
03 C1 57 8F AA
```

The KKL path can echo the transmitted `81 13 F7 81 0C` before the ECU response. The parser searches for a checksum-valid response rather than assuming the first bytes received are the answer.

### 15.3 StartDiagnosticSession

**[SRC][OBS]** Request:

```text
02 10 A0 B2
```

Observed positive response:

```text
01 50 51
```

### 15.4 SecurityAccess request seed

**[SRC][OBS]** Request:

```text
02 27 01 2A
```

Positive response form:

```text
04 67 01 SEED_H SEED_L CHECKSUM
```

Real project examples include seeds such as `48 2E`, `52 41`, `9F BB`, and `AA 48`.

### 15.5 TD5 seed-to-key algorithm

**[SRC][OBS]** The key algorithm implemented by Workbench is the public TD5 algorithm independently published by the TD5 reverse-engineering community.

The retained regression test vector is:

```text
seed 0x34A5 -> key 0x54D3
```

Send-key request format:

```text
04 27 02 KEY_H KEY_L CHECKSUM
```

Expected positive response:

```text
02 67 02 6B
```

One real project session produced:

```text
seed:  AA48
key:   64D5
TX:    04 27 02 64 D5 66
RX:    02 67 02 6B
```

The successful response is direct evidence that the algorithm and frame format work against the tested ECU.

---

## 16. TD5 frame/checksum rules

**[SRC][OBS]** After initialization, many tested TD5 messages use a compact length-prefixed frame:

```text
LENGTH  SERVICE  ...DATA...  CHECKSUM
```

The total frame size used by the Workbench parser is `LENGTH + 2` bytes, accounting for the length byte and final checksum.

The checksum is the low eight bits of the additive sum of all preceding frame bytes.

Negative response form recognized by Workbench:

```text
LENGTH 7F REQUESTED_SERVICE NRC CHECKSUM
```

A checksum-valid negative response still proves communication with the ECU. It must not be mistaken for a dead port.

---

## 17. TD5 service 0x21 data reads

**[SRC][OBS]** Current native live-data request format:

```text
02 21 PID CHECKSUM
```

Positive response form:

```text
LEN 61 PID DATA... CHECKSUM
```

Workbench retains raw payloads even when semantic decoding is unknown. That is a deliberate reverse-engineering feature.

### 17.1 Confirmed responding identifiers in current Workbench catalog

The v0.4.1.0 catalog includes confirmed responses for:

```text
01 03 04 05 07 09 0D 0E 10 18 1A 1B 1C 1D 1E 1F 20 21 23 24
32 33 36 37 38 39 3B 3C 3D 3E 3F 40 42 43 44 45 46 61
```

"Confirmed" here means the identifier responded in the project's survey/capture work. It does **not** mean every semantic field inside every payload has been fully decoded.

### 17.2 Higher-confidence signal mappings

Current catalog mappings include:

| PID | Current interpretation | Confidence note |
|---|---|---|
| `01` | coolant temperature | established project mapping |
| `05` | fuel temperature candidate | candidate label retained pending broader validation |
| `07` | manifold absolute pressure candidate | candidate label retained |
| `09` | engine RPM | high confidence and used as session health probe |
| `0D` | vehicle speed | high confidence |
| `10` | battery voltage | high confidence; additional word may be reference voltage |
| `1B` | accelerator track voltages | experimentally useful mapping |
| `1D` | fueling/demand words | several fields remain candidate mappings |
| `1E` | input/switch status including clutch/brake | clutch was useful in project overspeed-event analysis |
| `3B` | fault/DTC bitmap | directly observed; bit meanings still being mapped |
| `40` | injector trim words | raw signed words retained |
| `43` | large raw/experimental payload | preserved for reverse engineering |

When a field is labeled "candidate" in the source catalog, this document preserves that uncertainty.

---

## 18. TD5 fault bitmap, PID 0x3B

**[OBS]** PID `3B` returns a comparatively long payload and has been repeatedly observed to change when faults are intentionally induced.

Example project log after authentication:

```text
DTC SNAPSHOT: Current=3, Logged=4, Unknown=3, TotalFlags=10
bitmap=
00 00 00 00 00 00 00 00 00 1D 00 00 00 19 00 00 00 00
00 00 00 00 00 80 00 28 00 00 00 00 00 00 00 00 00
```

Later tests intentionally disconnected MAF and MAP sensors and compared bitmap changes before and after clear operations. The project now has a growing experimentally derived fault-position table. Until every bit is verified, Workbench should retain:

- raw full bitmap;
- current/logged/unknown classification where supported by current reverse engineering;
- first/last observation timestamps;
- induced-fault test context.

Do not discard an unknown set bit simply because no public description exists.

---

## 19. TD5 session health and recovery

**[OBS]** A TD5 diagnostic session can expire or be invalidated by key cycling. Workbench therefore does not trust a recently set `IsAuthenticated` flag by itself.

Current behavior:

1. probe a known data identifier, currently RPM PID `09`;
2. if the live probe succeeds, reuse the session;
3. if it fails, reopen the native COM handle and perform timing-critical fast init/session/security again.

The code also recognizes a negative response to StartCommunication as evidence that an ECU may still consider an earlier session active. It can attempt to resume using a live PID before performing a complete restart.

---

# Part IV: Chrysler/Jeep DRB III, SCI, and microPOD research

## 20. Separate the three things: DRB software, VCI/microPOD, and vehicle bus

The Chrysler research path currently has more layers than the ELM and KKL paths:

```text
DRB III Enhanced Emulator
        |
wiTECH / KDA / legacy VCI software stack
        |
lvci32.dll / J2534-style PassThru API
        |
TCP/IP-over-USB connection to microPOD
        |
microPOD vehicle interface
        |
SCI / CCD / PCI / ISO / CAN vehicle bus, depending on module and vehicle
```

Not every internal connection in this stack has been fully mapped. The project must distinguish what is observed from what is inferred.

---

## 21. microPOD network/device facts

### 21.1 Official/user-guide facts

**[STD]** The microPOD II user guide states that its Windows USB driver provides **TCP/IP over USB**. When connected over USB, its configuration web interface is reachable at:

```text
http://172.22.22.22
```

That explains why a physically USB-connected tool appears to diagnostic software as a networked device.

### 21.2 Project-observed installed driver path and version

**[OBS]** On the tested wiTECH installation:

```text
FunctionLibrary:
C:\Program Files (x86)\DCC Tools\wiTECH\jserver\app\legacyVCI\lvci32.dll

microPOD IP: 172.22.22.22
UseEncryption: 1
Firmware: 2.4.1 (SC:NONE)
DLL:      2.86
API:      04.04
```

`PassThruOpen` and `PassThruReadVersion` both succeeded during the project probe. This proves the installed Windows stack could open and identify the device. It does not alone prove a particular vehicle-bus command is correct.

### 21.3 DRB launch service observation

**[SRC][OBS]** Historical wiTECH/DRB setups commonly launch the enhanced emulator through a localhost service using port `9876`, passing the device address `172.22.22.22`. This matches the architecture seen in the project environment and explains why the browser/launcher can start a DRB emulator that then talks through the local wiTECH stack.

### 21.4 SSH/VComm evidence from installed binaries

**[OBS][INF]** String inspection of the project's installed `lvci32.dll`/related files exposed strings including:

```text
VcommPort
Close the tool and devmon sockets and ssh session...
Starting SSH connection using "new key"
Failed connection with "old key"
plink
```

Historical community notes also describe an SSH service on port `1677` and VComm server behavior.

What we can safely conclude:

- the legacy VCI DLL contains code for an SSH/plink-based connection mechanism;
- it refers to tool/devmon sockets and VComm-related configuration;
- the precise socket/tunnel topology used for every DRB vehicle request is still under investigation.

Do not treat a guessed internal TCP port as the vehicle diagnostic protocol until captures tie it to actual request/response traffic.

---

## 22. Project DRB III emulator observations

**[OBS]** During a successful DRB III Enhanced Emulator session on the project Jeep, the captured workflow demonstrated that the emulator could:

- enter the engine/PCM diagnostic module;
- display learned vehicle configuration;
- show a Sensor Display with 46 items;
- show an Input/Output display with 32 items;
- read four DTCs;
- erase DTCs;
- enumerate actuator tests;
- run a radiator-fan-control actuator test;
- return to sensor display.

A later cable disturbance caused complete communication loss, which is useful evidence that the preceding values were live ECU communication rather than static database content.

The screenshot timeline and Wireshark capture should be retained because they provide a ground-truth sequence for correlating network traffic with user actions.

---

## 23. J2534/SCI direct-probe results

**[OBS]** The project successfully opened a PassThru device and called `PassThruConnect` using the legacy SCI engine protocol configuration at:

```text
protocol = 7
baud     = 7812
```

The channel opened successfully, but an early candidate transmit `2A 01` received no matching ECU response. This result means:

- the microPOD driver/device path was alive;
- the requested SCI channel could be created;
- the candidate request was not yet proven to be a correct Jeep PCM command/session state.

A "successful J2534 connect" must therefore not be recorded as "we can read TPS" until a request produces a repeatable vehicle response correlated with physical throttle movement.

---

## 24. Chrysler SCI facts retained from public reverse engineering

### 24.1 Low-speed diagnostic mode

**[SRC]** Public Chrysler SCI reverse engineering documents a default diagnostic speed of approximately:

```text
7812.5 baud
```

Low-speed mode supports diagnostic functions such as DTCs, lower-resolution sensor values, and actuator tests.

### 24.2 Important low-speed command families

Retained examples from the Chrysler Scanner reverse-engineering database:

| Command | Retained interpretation |
|---|---|
| `10` | output stored fault-code list on supported controllers |
| `12` | switch/setup high-speed data transfer |
| `13` | actuator-test/setup family on supported controllers |
| `14 xx` | request a low-speed diagnostic value identified by location `xx` |
| `15 ...` | request a 16-bit memory location on supported controllers |
| `16 ...` | ECU identification family |
| `17` | clear error codes on supported controllers |
| `18 ...` | ASD relay control family on supported controllers |

These command meanings are controller-family dependent. They are starting points for the Jeep JTEC reverse-engineering work, not a guarantee that every Chrysler ECU implements every command identically.

### 24.3 Sensor examples relevant to our Jeep work

Public DRB database extraction includes:

```text
14 11 00   low-speed engine RPM
14 46      TPS percent
```

A documented example for `14 46` shows the PCM echoing the request bytes followed by a one-byte value. The DRB database supplies scaling rules to turn that byte into a displayed percentage.

This is directly relevant to the project's goal of correlating a known DRB TPS display with captured bus traffic.

### 24.4 High-speed SCI

**[SRC]** Command `12` is documented on relevant Chrysler controllers as the transition from about 7812.5 baud to:

```text
62500 baud
```

High-speed mode can expose more direct/high-resolution RAM data than the scaled single-byte low-speed commands. Public examples show returning to low-speed with a special high-speed command on some controller families.

The exact JTEC high-speed packet syntax still needs to be captured/validated for the project vehicle before it becomes a Workbench implementation.

### 24.5 DRB database as a protocol oracle

**[SRC]** Reverse-engineering work on DRB III database files has shown that the database can contain:

- module/bus selection;
- transmit bytes;
- parameter labels;
- scaling rules;
- units;
- sometimes rules for bit extraction and conversion.

That makes the DRB database useful for converting a screen item such as "TPS PERCENT" into a candidate request and response formula. It is still necessary to validate candidates against a real ECU because database entries cover many controller variants.

---

# Part V: Land Rover GEMS dealer diagnostics

## 25. Provenance and current status

The GEMS section is based on three categories of evidence:

1. **[SRC]** historical FlemcoDesign GEMS utility references and descriptions still available on the web;
2. **[OBS]** preserved project artifacts including `GEMS.zip` and the Android GEMS ECU Utility APK, plus decompiled/extracted command strings from the project research session;
3. **[INF]** interpretation of those command strings as an ELM-driven ISO 14230/KWP-style dealer session.

The command sequence below is substantially stronger than a forum guess because the same structure appeared in the preserved utility artifacts. It should still be hardware-tested in a dedicated GEMS probe before being promoted into normal Workbench write functionality.

---

## 26. GEMS ELM/ISO setup sequence retained from the utility artifacts

**[OBS][INF]** The extracted dealer-session initialization sequence is:

```text
ATE0
ATS0
ATAL
ATH0
ATSP4
ATIIADA
ATSHC133F1
ATSI
```

Interpretation:

- echo off;
- spaces off;
- allow long messages;
- headers hidden from host display;
- select ELM protocol 4, ISO 14230/KWP with slow initialization;
- set slow-init address to `DA`;
- set outgoing diagnostic header to `C1 33 F1`;
- perform ISO slow initialization.

This is a dealer-diagnostic path layered through an ELM-compatible adapter, distinct from ordinary generic OBD Mode 01/03 traffic.

---

## 27. GEMS diagnostic session and security

### 27.1 Session start

**[OBS]** Extracted request/positive response:

```text
10 02  ->  50 02
```

This is consistent with a KWP-style diagnostic-session start.

### 27.2 Request seed

**[OBS]** Extracted service:

```text
27 01  ->  67 01 [seed bytes]
```

### 27.3 Seed-to-key formula

**[OBS]** The utility logic yielded the following 16-bit calculation:

```text
key = (seed * 0x4153) & 0xFFFF
```

### 27.4 Send key

**[OBS]** Extracted form:

```text
27 02 [key-high] [key-low]
```

with a successful response pattern:

```text
67 02 AA
```

The Windows and Android artifact investigations were consistent on the security multiplier, which raises confidence that this is intentional GEMS authorization logic rather than an accidental constant.

---

## 28. GEMS dealer data reads retained

**[OBS]** Service `22` data identifiers extracted from the utility include:

| Request | Utility interpretation |
|---|---|
| `22 23 32` | air adaptive value |
| `22 23 33` | fuel adaptive value |
| `22 23 34` | learned closed-throttle / TPS value |
| `22 23 36` | additional utility data item, semantic mapping not yet finalized in Workbench notes |
| `22 23 37` | additional utility data item, semantic mapping not yet finalized |
| `22 04 BF` | observed in APK command set; exact semantic meaning unresolved |
| `22 04 C4` | observed in APK command set; exact semantic meaning unresolved |
| `22 04 E2` | observed in APK command set; exact semantic meaning unresolved |

Unknown identifiers should remain in the reference rather than be discarded. They can become test candidates later.

Historical GEMS command-line utility output on Land Rover Forums independently shows the utility reporting air adaptive, fuel adaptive, and TPS closed values before resetting adaptations, which supports these high-level interpretations.

---

## 29. GEMS adaptive reset

**[OBS]** Extracted payload:

```text
A3 23 48 00
```

The preserved Windows implementation exposed it in a length/checksum-framed form:

```text
04 A3 23 48 00 12
```

where the leading byte is a length field and the final byte is an additive checksum in the utility's transport mode.

This operation changes learned/adaptive ECU state and must be treated as a write/action, not a harmless read.

---

## 30. GEMS immobilizer / BCU synchronization

**[OBS]** Extracted payload:

```text
A3 00 62 25 88
```

Preserved framed form:

```text
05 A3 00 62 25 88 B7
```

The public GEMS Utility description says the application can pair the immobilizer/BCU to the ECU, which is consistent with the purpose inferred for this request.

This is a security/vehicle-configuration action. It should remain segregated from passive discovery and require explicit user initiation if ever implemented in Workbench.

---

## 31. GEMS framing/checksum note

**[OBS][INF]** The preserved utility code contains explicit checksum handling and examples where a length byte is prepended and an additive checksum appended. It also contains adapter configuration intended to avoid conflicting automatic ELM/STN handling while sending dealer-level packets.

The practical project rule is:

> retain both the logical diagnostic payload and the exact adapter-level bytes used by the source utility.

That prevents future confusion between KWP payload bytes, KWP header bytes, ELM ASCII representation, and any length/checksum bytes managed manually by the application.

---

# Part VI: Cross-protocol engineering rules

## 32. Echo is data-path noise, not an ECU response

Native single-wire interfaces often return the bytes just transmitted. The parser should therefore:

1. preserve raw receive bytes in logs;
2. identify and mark a full TX echo;
3. continue searching after the echo for a checksum-valid ECU response;
4. never call an echoed request "success."

This rule has been essential on both SSM and TD5 testing and is likely to matter for Chrysler SCI as well.

---

## 33. A checksum-valid negative response is useful evidence

If the ECU returns a valid negative response, then:

- physical wiring is probably alive;
- baud/framing are likely close enough to communicate;
- addressing/session state may be valid;
- the request, sequence, security state, or service may be wrong.

Treating all non-positive replies as "no communication" throws away valuable diagnostic information.

---

## 34. Source provenance must survive decoding

For multi-module vehicles, every value/DTC/acknowledgement should retain:

```text
transport source address / CAN responder ID
module label if known
request that produced it
timestamp
raw payload
parsed value
```

This is not merely logging decoration. It prevents a PCM result from being accidentally applied to a TCM and vice versa.

---

## 35. Discovery should be conservative

When the vehicle or ECU identity is not known:

- start with read-only queries;
- do not guess unsupported write services;
- do not hide unknown responders;
- preserve raw bytes even if a semantic mapping is absent;
- prefer "unknown" over a confident but invented signal label.

OBDWorkbench's conservative mode and probe utilities are built around this principle.

---

## 36. Actions/writes need a higher evidence threshold

Read operations can usually fail harmlessly. Clear-memory, actuator, configuration, immobilizer, and programming operations can change vehicle state.

Before adding a write/action to the main application, the project should ideally have:

1. an external protocol/source reference or strong reverse-engineered evidence;
2. a standalone test utility;
3. raw TX/RX logging;
4. explicit preconditions, such as engine off or stationary;
5. no automatic retries when duplicate execution could matter;
6. post-action verification;
7. a known way to recover from interruption when applicable.

The SSM clear test is the model for this workflow.

---

## 37. Raw-data retention policy

Every experimental probe should save enough information to reinterpret data later:

- exact outgoing bytes;
- exact incoming bytes, including echo;
- timestamps and response timing;
- baud/protocol/transport;
- ECU/module source;
- selected session/security state;
- decoded fields and formulas;
- failed attempts, not just successes;
- notes about physical actions performed during the capture.

A decoder can improve years later. A missing raw capture cannot be recreated.

---

# Part VII: Current project evidence and retained local artifacts

## 38. OBDWorkbench source baseline

The reference was initially built against:

```text
OBDWorkbench_v0.4.1.0_source.zip
SHA-256:
dd19e58b00473804c634510e97c0db44a119ad96a3563d89c6af496ac186707a
```

Important existing files:

- `README.md`
- `ARCHITECTURE.md`
- `RELEASE_NOTES_v0.4.1.0.md`
- `BUILD_VALIDATION.txt`
- `OBDWorkbench.Core/Services/Elm327Connection.cs`
- `OBDWorkbench.Core/Services/MultiEcuParser.cs`
- `OBDWorkbench.Core/Services/SsmProtocol.cs`
- `OBDWorkbench.Core/Services/SsmConnection.cs`
- `OBDWorkbench.Core/Services/SsmParameterCatalog.cs`
- `OBDWorkbench.Core/Services/Td5KLineConnection.cs`
- `OBDWorkbench.Core/Services/Td5SignalCatalog.cs`

`ARCHITECTURE.md` describes the application/transport split. This document is intended to complement it with protocol knowledge and provenance.

---

## 39. SSM evidence retained by the project

Relevant local artifacts include:

- `SSMKLineProbe_v0.1.x` source packages and SSM raw/live/session logs;
- BF capability captures from 2026-09-22 and 2026-09-23;
- systematic A8 batching tests;
- `SSMDtcProbe_v0.1.0_source.zip`;
- DTC bytes/flags/identification/session files from scanner comparison and clear tests;
- later resistor-assisted fuel-temperature clear test proving persistent removal of P0183 after the fault condition was corrected.

The DTC probe source archive used while building this document has SHA-256:

```text
41f0ebf494fff24d190f5e83a17ba507d62cef2520c83f44258fc13f13bff720
```

---

## 40. TD5 evidence retained by the project

Relevant local artifacts include:

- `KLineProbe` versions and raw/session/live logs;
- repeated fast-init/session/security captures;
- driving captures including clutch/RPM timing;
- `TD5FaultProbe` logs and CSVs;
- deliberately induced MAF/MAP fault experiments;
- fault-clear comparison captures;
- Workbench v0.4.x native TD5 implementation and signal catalog.

The strongest proof of the session/security sequence is not merely the public source. The project has multiple real ECU captures showing seed receipt, calculated key transmission, `67 02` success, and subsequent Service 21 reads.

---

## 41. Chrysler/microPOD evidence retained by the project

Relevant local artifacts include:

- `drb_connected_reading_testing.pcapng` and copy;
- `Screenshot (162).zip`;
- `drb3_screenshots_explained.txt` with the exact DRB menu/action timeline;
- microPOD environment/driver probes;
- SCI PassThru test logs;
- extracted installed-driver strings used to understand the VComm/SSH layer;
- `JeepMicroPodCollector_v0.1.0.zip`.

The collector archive used during this documentation pass has SHA-256:

```text
574c4db5bfdd7b96528887a792a5329a773be419e37397fe5df2bc8f92f4a314
```

---

## 42. GEMS evidence retained by the project

Relevant local project artifacts include:

- `GEMS.zip` historical utility package;
- `GEMS ECU Utility for Land Rove` Android APK, package `com.flemcodesign.gems.gemsutility`, version 1.5;
- reverse-engineering notes from the Windows utility/APK command extraction;
- public Land Rover Forums history pointing to the former FlemcoDesign downloads.

These local copies are particularly important because the original developer-hosted `flemcodesign.com/files/GEMS.zip` and `GEMS11.zip` links are historical dependencies and may not remain available indefinitely.

---

# Part VIII: Web and external source registry

## 43. Archive/source policy

Each entry below records the source, what was retained, and why it matters. An unavailable future URL should not make the technical section above unintelligible.

Access date for entries gathered during this document pass: **2026-09-24**, unless another date/revision is explicitly stated.

### Source quality convention

- **Primary/vendor:** original vendor documentation or official hardware manual.
- **Reference implementation:** source code implementing the protocol or an established reverse-engineering project.
- **Community reverse engineering:** technically detailed independent work, useful but not manufacturer documentation.
- **Project observation:** our own local logs/captures/artifacts.

---

## 44. ELM327 / standard OBD-II sources

### WEB-ELM-01: ELM327 AT Commands, ELM Electronics

URL:  
`https://cdn.sparkfun.com/assets/4/e/5/0/2/ELM327_AT_Commands.pdf`

Type: vendor command reference mirror.  
Attribution shown in document: ELM Electronics Inc., October 2010.

Facts retained here:

- adapter commands use `AT` command vocabulary;
- `DP/DPN` describe selected protocol;
- `E0/E1` control echo;
- `H0/H1` control headers;
- `L0/L1` control linefeeds;
- `SP` selects a vehicle protocol;
- `SH` sets header;
- `IIA` sets ISO slow-init address;
- `IB 10` selects 10400 ISO baud and `IB 48` 4800 on supporting versions;
- `FI` performs fast init on supporting versions;
- monitor and timeout commands exist.

Preservation note: enough command meaning is copied into Sections 4 and 26 for Workbench/GEMS development even if the PDF disappears.

### WEB-ELM-02: ELM327 datasheet

URL:  
`https://cdn.sparkfun.com/assets/learn_tutorials/8/3/ELM327DS.pdf`

Type: vendor datasheet mirror.

Facts retained here:

- ELM is an ASCII-command OBD interpreter;
- host commands and vehicle messages are distinct concepts;
- prompt and AT command behavior underpin Workbench's ELM transport.

### WEB-OBD-01: CSS Electronics OBD2 Explained

URL:  
`https://www.csselectronics.com/pages/obd2-explained-simple-intro`

Type: technical secondary reference.

Facts retained here:

- positive OBD responses commonly add `0x40` to the request service;
- PID `00`, `20`, `40`, etc. expose supported PID bitmaps;
- Mode 09 PID 02 is the standard VIN request;
- CAN VIN can require ISO-TP multi-frame transport.

### WEB-OBD-02: CSS Electronics OBD2 PID table

URL:  
`https://www.csselectronics.com/pages/obd2-pid-table-on-board-diagnostics-j1979`

Type: technical secondary reference derived from standardized OBD definitions.

Facts retained here:

- standard Mode 01 raw conversions used as parser sanity checks;
- examples such as RPM, coolant, speed, MAF, throttle.

### Standards named but not reproduced

Relevant formal standards include SAE J1979/related emissions OBD definitions, SAE J1962 connector conventions, ISO 9141-2, ISO 14230/KWP, and ISO 15765/ISO-TP. These standards are copyrighted publications. This project records the small interoperable facts required by its implementation but does not redistribute the standards.

---

## 45. Subaru SSM sources

### WEB-SSM-01: LibSSM2 `SSM2_Protocol.txt`

URL:  
`https://github.com/src0x/LibSSM2/blob/master/SSM2_Protocol.txt`

Type: reference implementation / historical protocol note.

The file itself notes that the protocol text appears to originate from the ecuExplorer project and was also historically present in RomRaider.

Facts retained here:

- SSM2 uses an ISO 9141 interface at 4800 baud, 8N1;
- frame format `80 dest source data-size data checksum`;
- ECU address `10`, diagnostic tool address `F0`;
- checksum is low 8 bits of additive byte sum;
- BF initialization request example.

The source warns that modern ECUs may have more init/capability flag bytes than the old document listed. Workbench therefore treats the basic framing as stable but does not assume the old capability table is exhaustive.

### WEB-SSM-02: RomRaider `definitions/log_defs.xml`

URL:  
`https://github.com/RomRaider/RomRaider/blob/master/definitions/log_defs.xml`

Type: reference implementation/data definitions.

Facts retained here:

- SSM base memory addresses;
- data widths;
- conversion formulas;
- BF flag byte/bit associations for many parameters.

Examples retained in Section 10 include RPM `000E/000F` divided by 4, coolant `000008 - 40`, throttle `000015 *100/255`, MAF `000013/14 /100`, and battery `00001C * 0.08` V.

### WEB-SSM-03: FreeSSM repository

URL:  
`https://github.com/Comer352L/FreeSSM`

Type: GPL-3.0 open-source reverse-engineering/reference implementation.

Project-pinned revision used for DTC-probe provenance:

```text
1a0fa0934581b3383adfd2722050503695ca9dab
```

Specific files referenced:

`https://github.com/Comer352L/FreeSSM/blob/1a0fa0934581b3383adfd2722050503695ca9dab/src/SSMFlagbyteDefinitionsInterface.cpp`

Retained use: capability-dependent fault-bank selection and clear-memory address/value.

`https://github.com/Comer352L/FreeSSM/blob/1a0fa0934581b3383adfd2722050503695ca9dab/src/SSMFlagbyteDefinitions_en.cpp`

Retained use: factual measuring-block definitions and DTC address/bit/code correspondences. The project-generated CSV preserves only factual mapping fields, not FreeSSM's descriptive prose.

`https://github.com/Comer352L/FreeSSM/blob/1a0fa0934581b3383adfd2722050503695ca9dab/src/SSMP2communication_core.cpp`

Retained use: B8 single-address write structure and F8 write acknowledgement.

`https://github.com/Comer352L/FreeSSM/blob/1a0fa0934581b3383adfd2722050503695ca9dab/src/SSMCUdata.cpp`

Retained use: system-ID family and BF capability offsets.

`https://github.com/Comer352L/FreeSSM/blob/1a0fa0934581b3383adfd2722050503695ca9dab/doc/help_en.html`

Retained use: memory-clear/adaptation behavior and caution that SSM clear behavior is not the same thing as generic OBD status terminology.

License/provenance note: FreeSSM is GPL-licensed. OBDWorkbench's DTC probe was newly written and its mapping CSV contains protocol facts rather than copied implementation/prose. This document records provenance and is not a legal licensing opinion.

---

## 46. Land Rover TD5 sources

### WEB-TD5-01: pyTD5Tester

URL:  
`https://github.com/hairyone/pyTD5Tester/blob/master/TD5Tester.py`

Type: open-source/reference implementation.

Facts retained here:

- K-Line initialization timing and 10400-baud communication;
- fast-init handling;
- request/response echo handling patterns;
- Service 21 style data requests;
- additive checksums;
- diagnostic session/security flow.

### WEB-TD5-02: Ekaitza Itzali

URL:  
`https://github.com/EA2EGA/Ekaitza_Itzali`

Type: open-source TD5 diagnostic project.

Facts retained here:

- TD5 diagnostic tooling can read fueling/switch data, read/clear faults, test outputs, read/write selected settings, and perform mapping work;
- project credits prior TD5 reverse-engineering work and the public seed/key implementation.

### WEB-TD5-03: td5keygen

URL:  
`https://github.com/pajacobson/td5keygen`

Type: open-source security-algorithm reference.

Facts retained here:

- request-seed / send-key framing context;
- positive response `02 67 02 6B`;
- regression vector `34A5 -> 54D3`.

Workbench includes this vector as a self-test and has separately proven successful keys against a real ECU.

### WEB-TD5-04: TD5-Dash technical reference

URL:  
`https://github.com/JRogers83/TD5-Dash/blob/main/documentation/TD5-ECU-Protocol-Technical-Reference.md`

Type: modern community consolidation/secondary reference.

Use: cross-checks known seed/key test vectors and protocol terminology. It is useful as a consolidated backup but should not outrank our own captures plus the earlier public implementations.

### Historical TD5 source path

Several older projects credit DiscoTD5.com diagnostic research. Historical URL references should be retained in project notes even when unavailable, because newer projects may only preserve derived implementations.

---

## 47. Chrysler/DRB/microPOD sources

### WEB-MPOD-01: microPOD II User Guide

Current mirror found during this pass:  
`https://fccid.io/ANATEL/02705-14-05621/Manual-MPOD2-C/46979A67-75F0-49BF-9F70-FFF254EAE1BE/PDF`

Alternate text mirror found:  
`https://manuals.plus/m/55b9ee1fae52ea82ab7885db8cfaae1a3badfffd7653fa230cf56f4b0928f5f3`

Type: hardware user-guide copy / primary device documentation.

Facts retained here:

- USB connection provides TCP/IP networking through the Windows driver;
- device configuration web interface uses `http://172.22.22.22`;
- the pod can be powered over vehicle or USB and runs an embedded software environment.

### WEB-MPOD-02: historical wiTECH/microPOD DRB emulator community notes

Representative URL:  
`https://micropod2.blogspot.com/2018/10/download-install-drbiii-emulator.html`

Type: community operational documentation.

Facts retained here:

- historical enhanced DRB emulator installations were launched through a localhost wiTECH service on port `9876` with the pod IP supplied as an argument;
- this aligns with the project's observed local wiTECH architecture.

Treat clone-specific installation advice on these pages as historical/community material, not official FCA behavior.

### WEB-MPOD-03: historical microPOD internal-service notes

Representative URL:  
`https://micropod2.blogspot.com/2018/11/`

Type: community reverse engineering.

Facts retained here:

- historical reports identify an SSH service on port 1677 and VComm logs/services;
- our own installed `lvci32.dll` independently contains SSH/plink/VComm-related strings, so the general architecture is plausible.

Security material or private keys published by third parties are intentionally not reproduced in this reference. They are unnecessary for documenting the communication architecture.

### WEB-SCI-01: Chrysler Scanner, SCI-bus commands

URL:  
`https://chryslerccdsci.wordpress.com/2019/07/31/sci-bus-commands/`

Type: detailed community reverse engineering.

Facts retained here:

- default low-speed SCI diagnostic communication around 7812.5 baud;
- command families `10`, `12`, `13`, `14`, `15`, `16`, `17`, `18`;
- low-speed sensor examples including RPM `14 11 00` and TPS `14 46`;
- low-speed values may be scaled/simplified versus high-speed RAM data.

### WEB-SCI-02: Chrysler Scanner, DRB-III database reader

URL:  
`https://chryslerccdsci.wordpress.com/2019/07/21/drb-iii-database-reader/`

Type: detailed community reverse engineering.

Facts retained here:

- DRB database entries can expose transmit bytes, parameter labels, scaling, unit conversion and bit rules;
- example request/echo/value behavior for TPS;
- database content can be used as a candidate-command oracle, but must be validated per controller.

### WEB-SCI-03: Chrysler Scanner, high-speed SCI

URL:  
`https://chryslerccdsci.wordpress.com/2019/09/27/sneak-peek-into-the-high-speed-realm-of-the-sci-bus/`

Type: detailed community reverse engineering.

Facts retained here:

- command `12` transitions relevant controllers from 7812.5 baud to 62500 baud;
- high-speed mode provides direct/higher-resolution RAM-oriented data in documented examples.

---

## 48. Land Rover GEMS sources

### WEB-GEMS-01: Land Rover Forums historical GEMS utility thread

URL:  
`https://landroverforums.com/forum/discovery-i-39/de-computerize-85158/page5/?styleid=7`

Type: historical community/developer thread.

Facts retained here:

- historical download links to FlemcoDesign `GEMS.zip` and later `GEMS11.zip`;
- utility used OBDLink SX/LX style ELM-compatible interfaces;
- command-line example reports air adaptive value, fuel adaptive value and TPS closed value, then resets adaptations;
- developer commentary describes a special GEMS diagnostic mode beyond ordinary generic OBD.

Historical developer links recorded there:

```text
http://www.flemcodesign.com/files/GEMS.zip
http://www.flemcodesign.com/files/GEMS11.zip
```

These URLs are retained for attribution even though availability can change. The project also retains local artifacts.

### WEB-GEMS-02: GEMS ECU Utility for Land Rover, APK listing

URL:  
`https://apkcombo.com/gems-ecu-utility-for-land-rove/com.flemcodesign.gems.gemsutility/`

Type: archived application listing/secondary distribution reference.

Facts retained here:

- developer: FlemcoDesign;
- package: `com.flemcodesign.gems.gemsutility`;
- version 1.5;
- app description explicitly states use of an ELM327-compatible Bluetooth interface;
- advertised functions include reading dealer-level settings, resetting adaptive values, and pairing immobilizer/BCU to the ECU;
- listed target applications include late-1990s GEMS Land Rover vehicles plus GEMS-equipped Morgan/TVR applications.

### WEB-GEMS-03: AppBrain historical listing

URL:  
`https://www.appbrain.com/app/gems-ecu-utility-for-land-rove/com.flemcodesign.gems.gemsutility`

Type: historical application metadata.

Facts retained here:

- confirms the package/developer history and that the app later disappeared from Google Play;
- this is exactly the sort of source fragility that motivates preserving the protocol facts locally.

### LOCAL-GEMS-01: preserved utility artifacts

Type: project observation / local archive.

Retained technical facts from local reverse engineering are already copied into Sections 26 through 31:

- ELM setup commands;
- `C1 33 F1` header and `DA` slow-init address;
- session `10 02`;
- SecurityAccess `27 01`/`27 02`;
- multiplier `0x4153` seed-to-key calculation;
- adaptive data identifiers;
- `A3 23 48 00` adaptive reset;
- `A3 00 62 25 88` immobilizer/BCU synchronization;
- manual length/additive-checksum framing examples.

This local extraction is more important than the web listing for the byte-level protocol, because the public app descriptions document capabilities but not the internal request bytes.

---

# Part IX: Current unknowns and next research targets

## 49. SSM

Known gaps:

- complete behavior and compatibility of actuator-test commands across ECU families;
- VIN/odometer availability and command differences by Subaru generation;
- exact semantics/status terminology for all current/history DTC banks;
- whether the empirically observed 43-address A8 ceiling is ECU, adapter, driver, or timing specific;
- broader hardware validation of all 156 catalog definitions.

Next safe direction: read-only identification and additional capability-driven reads first; actuator commands should be introduced through dedicated tests with explicit user initiation.

---

## 50. TD5

Known gaps:

- authoritative bit-to-fault mapping for every PID `3B` position;
- complete semantic decoding for many confirmed Service 21 identifiers;
- robust ECU-family variation handling across early MSB, later NNN, automatic/manual configurations;
- additional write/actuator services and their safety constraints.

Continue to preserve raw payloads from deliberately induced faults because those experiments are producing better evidence than unverified internet tables.

---

## 51. Chrysler/Jeep

Known gaps:

- exact DRB/KDA/microPOD software message path between emulator actions and J2534/VComm calls;
- correct direct SCI session/command sequence for the tested Jeep PCM;
- correlation of known DRB screen values such as TPS with low-level traffic;
- distinction between low-speed SCI and any high-speed data path used by the actual DRB session;
- module-specific SCI/CCD division of responsibility on the vehicle.

The most valuable next evidence is a capture where a single physical value is changed in a controlled way while DRB displays it, with packet timing synchronized to the screen action.

---

## 52. GEMS

Known gaps:

- live hardware confirmation of the extracted ELM/KWP dealer-session sequence in our own test utility;
- exact meanings and formulas for several `22` data identifiers;
- response framing under generic ELM versus STN/OBDLink optimized modes;
- safe handling of adaptation reset and immobilizer synchronization failures;
- whether additional dealer functions can be extracted from the preserved APK/desktop binaries.

Until hardware-confirmed, the GEMS write commands should remain documentation/test-probe material rather than automatic Workbench features.

---

# Part X: Maintenance rules for this document

## 53. When a new protocol fact is learned

Update this file when any of the following occurs:

- a new service/request works on hardware;
- a new response byte or bit is mapped;
- a public source resolves an unknown;
- a public source conflicts with a real ECU observation;
- a Workbench parser rule is added because of an adapter quirk;
- a write/clear/actuator operation is experimentally validated;
- a source URL disappears or moves;
- a newer source/revision changes an earlier interpretation.

Every update should include evidence level and, when possible, a concrete raw request/response example.

---

## 54. How to handle conflicting sources

When two references disagree:

1. preserve both claims and identify their sources;
2. check whether they apply to different ECU generations or units;
3. prefer raw hardware observation for the exact ECU being supported;
4. do not delete the losing interpretation until the reason for the conflict is understood;
5. preserve raw bytes so the decision can be revisited.

---

## 55. What belongs here versus elsewhere

This file should contain:

- protocol framing and timing;
- command/service meanings;
- address/PID discovery behavior;
- response parsing rules;
- safety/write behavior;
- reverse-engineering evidence;
- source provenance;
- representative signal/DTC mappings necessary to understand communication.

Large machine-readable tables may live beside it in `ReferenceData/`, but this file must describe their format, provenance, and how they are used.

Application UI design, release-specific feature lists, and build instructions belong in README/release/architecture documents rather than being duplicated here.

---

# Appendix A: Quick byte-sequence cheat sheet

## A.1 SSM2

```text
Serial: 4800 8N1
Tester: F0
ECU:    10
Frame:  80 DEST SRC LEN DATA... CHECKSUM
Checksum: additive low byte

Init:
80 10 F0 01 BF 40

A8 read data:
A8 00 [24-bit-address] ...
Positive service: E8

Clear engine memory:
80 10 F0 05 B8 00 00 60 40 DD
Expected ACK:
80 F0 10 02 F8 40 BA
```

## A.2 TD5

```text
Serial: 10400 8N1
Fast init: approx low 25 ms / high 25 ms

Start communication:
81 13 F7 81 0C
Observed response:
03 C1 57 8F AA

Start diagnostic session:
02 10 A0 B2
Response:
01 50 51

Request security seed:
02 27 01 2A
Response form:
04 67 01 SH SL CS

Send key:
04 27 02 KH KL CS
Response:
02 67 02 6B

Read identifier:
02 21 PID CS
Response form:
LEN 61 PID DATA... CS
```

## A.3 Chrysler SCI research

```text
Default low-speed diagnostic mode: about 7812.5 baud
High-speed transition on relevant controllers: command 12 -> about 62500 baud

Examples from public DRB database reverse engineering:
14 11 00   RPM
14 46      TPS percent
```

## A.4 GEMS dealer session via ELM

```text
ELM setup:
ATE0
ATS0
ATAL
ATH0
ATSP4
ATIIADA
ATSHC133F1
ATSI

Diagnostic session:
10 02 -> 50 02

Security seed:
27 01 -> 67 01 [seed]
key = (seed * 0x4153) & 0xFFFF
27 02 [key] -> 67 02 AA

Dealer reads:
22 23 32   air adaptive
22 23 33   fuel adaptive
22 23 34   learned closed TPS

Adaptive reset:
A3 23 48 00
framed source example: 04 A3 23 48 00 12

Immobilizer/BCU sync:
A3 00 62 25 88
framed source example: 05 A3 00 62 25 88 B7
```

---

# Appendix B: Project test matrix, current state

| System | Transport/interface | Read data | DTC read | Clear | Actuator/write | Evidence state |
|---|---|---:|---:|---:|---:|---|
| Standard OBD-II via ELM | serial + Wi-Fi/TCP ELM | yes | yes | yes, module-aware work ongoing | generic OBD scope only | heavily observed |
| Subaru SSM2 engine | native KKL, 4800 8N1 | yes | yes | yes, persistent clear verified when fault corrected | actuator research next | heavily observed |
| Land Rover TD5 engine | native K-Line, 10400 8N1 | yes | PID 3B bitmap | experimental fault-clear work | broader actions not in main Workbench | heavily observed for session/read |
| Jeep DRB III via microPOD | wiTECH/legacy VCI/microPOD | DRB itself yes; direct Workbench path incomplete | DRB itself yes | DRB itself yes | DRB fan actuator observed | stack under reverse engineering |
| Chrysler SCI direct | microPOD/J2534 test path | channel opens; direct value request not yet proven | not yet integrated | not yet integrated | not yet integrated | experimental |
| Land Rover GEMS | ELM/KWP-derived dealer sequence | artifact-derived commands | dealer-level behavior inferred from utility | adaptive reset command extracted | immobilizer/BCU sync command extracted | byte sequence documented, hardware validation pending |

---

# Appendix C: Revision history

## 2026-09-24, initial canonical reference

Consolidated:

- OBDWorkbench v0.4.1.0 source behavior;
- ELM327/standard OBD parsing lessons;
- Subaru SSM framing, capability discovery, A8 reading and DTC bank work;
- SSM clear command and successful resistor-assisted P0183 clear verification;
- TD5 native K-Line initialization, session, seed/key and Service 21 work;
- TD5 PID 3B fault-bitmap research;
- DRB III/microPOD architecture and captures;
- Chrysler SCI public reverse-engineering facts;
- GEMS preserved-utility command extraction;
- durable web-source registry and archive policy.

