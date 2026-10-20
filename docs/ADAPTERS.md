# Adapter Guide

## SSM

Subaru SSM2 uses a raw K-Line serial adapter at 4800 baud. The validated development hardware is an FT232RL-based VAG-COM/KKL-style interface. Select the **SSM** splash tile; Workbench will perform BF capability discovery and A8 reads directly rather than sending ELM commands.

## TD5

TD5 native diagnostics in this build use a raw K-Line adapter. The validated development hardware is an FT232RL-based VAG-COM/KKL-style interface capable of direct K-Line timing control.

TD5 mode does **not** use ELM AT commands. Select the **TD5** splash tile so Workbench does not probe the adapter as an ELM device first.

## GEMS

GEMS dealer diagnostics use an ELM327/STN command interface. OBDLink/STN hardware is preferred because it is generally more faithful to the ELM/STN command set. Generic ELM327 clones vary substantially.

The initial GEMS implementation configures ISO 14230 protocol 4, 5-baud initialization address `DA`, and the recovered Land Rover dealer session. If the adapter rejects commands such as `ATIIA`, `ATSH`, or `ATSI`, create a Support ZIP and include the adapter make/model in the issue.

## Generic OBDII

Use the **OBDII** tile for standard ELM327/STN OBD-II operation. Serial USB/Bluetooth-style COM ports and supported Wi-Fi/TCP adapters are handled separately from raw TD5 K-Line.
