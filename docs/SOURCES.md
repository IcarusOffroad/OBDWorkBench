# Sources and retained protocol references

OBD Workbench keeps source provenance separate from experimentally observed vehicle behavior. These links document the public material used to understand TD5/GEMS communication. The protocol facts implemented by Workbench are summarized offline in `PROTOCOL_REFERENCE.md` so a disappearing website does not erase the development record.

## Subaru SSM2

- LibSSM2 retained protocol notes: https://github.com/src0x/LibSSM2/blob/master/SSM2_Protocol.txt
- RomRaider base logging definitions: https://github.com/RomRaider/RomRaider/blob/master/definitions/log_defs.xml
- FreeSSM repository and pinned factual flag/address mappings: https://github.com/Comer352L/FreeSSM

The restored v0.4.1 SSM implementation uses project-written protocol/transport code plus retained factual addresses, capability flags and conversions. `COMMUNICATIONS_REFERENCE.md` records the exact provenance, observed BF/A8 behavior, DTC-bank research and the licensing/provenance caution around FreeSSM-derived factual mappings.

## TD5

- `pajacobson/td5keygen` — TD5 SecurityAccess seed/key work and reference implementation: https://github.com/pajacobson/td5keygen
- `EA2EGA/Ekaitza_Itzali` — Land Rover TD5 diagnostic application, including read/clear faults and other diagnostic services: https://github.com/EA2EGA/Ekaitza_Itzali
- `JRogers83/TD5-Dash` — later independent TD5 K-Line implementation and protocol notes: https://github.com/JRogers83/TD5-Dash
- TD5SPY fault/reference material: https://www.td5spy.co.za/td5_faults

Vehicle experiments performed during OBD Workbench development are recorded separately in logs and in `Td5FaultCatalog.cs`. They include deliberate MAF and MAP/IAT disconnects, high-speed-crank/overspeed behavior, and clear/relog testing. Experimental associations are labeled as such rather than silently replacing published labels.

## GEMS

- RobertF/FlemcoDesign GEMS ECU Utility Windows binary and Android v1.5 APK were retained in the private project research archive and analyzed to recover dealer-session behavior. They are **not redistributed** with OBD Workbench.
- Public development discussion: https://discoweb.org/index.php?threads/reset-adaptive-values.92885/
- Public Land Rover forum discussion preserving GEMS utility history: https://landroverforums.com/forum/discovery-i-39/de-computerize-85158/

Recovered application-layer facts implemented in this preview are summarized in `PROTOCOL_REFERENCE.md`, including the DA-address slow initialization, session/security sequence, dealer reads, and disabled write commands.

## Adapter/protocol documentation

- ELM Electronics ELM327 documentation: https://www.elmelectronics.com/wp-content/uploads/2016/07/ELM327DS.pdf
- ScanTool/STN product documentation archive: https://www.scantool.net/scantool/downloads/archive/product-documentation/

## Source conflicts

Old TD5 fault tables are not perfectly consistent. When a published label conflicts with a controlled vehicle experiment, Workbench preserves both the published label and a clearly marked experimental note/confidence level. Unknown positions remain unknown until evidence supports a more specific meaning.
