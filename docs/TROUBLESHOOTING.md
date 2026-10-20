# Troubleshooting

| Symptom | Check |
| --- | --- |
| Build fails before compiling | Install .NET 8 SDK; retain the whole source tree; restore with internet access; report the first full error and revision. |
| Generic/default taskbar icon remains | Run the newly rebuilt executable. Unpin an old taskbar shortcut and pin the new EXE. Windows may cache the old icon; close/reopen the app or sign out/in. The GPS monitor must be rebuilt/updated separately. |
| GPS source is Service but debug says disabled | Check Enable GPS in Workbench. Choosing the source alone does not subscribe. |
| Service unavailable | Check Windows Services for IcarusGPS, start it, and confirm its protocol version supports Icarus.GPS.Data.v1. Workbench will retry. |
| Service connected, no location | Check receiver state/NMEA. A service heartbeat does not imply a satellite fix. Check antenna, reception, satellites and acquisition time. |
| COM access denied | Close PuTTY/other consumers, or release the physical receiver in the GPS tray before using Direct COM. Do not use the diagnostic adapter's COM port for GPS. |
| Receiver shows released | Resume it from the GPS tray monitor. |
| Unknown ECU fingerprint | Read/log where supported and report a Support ZIP; do not assume writes are validated. |
| ELM bus init/CAN errors | Confirm correct vehicle/protocol, adapter switches and wiring. Clones vary; follow the adapter/family guide and retain raw evidence. |
| Chart/gauge is frozen | Confirm source connection, polling/recording state and GPS validity. Historical values can remain after disconnect. |
| Replay has no GPS samples | Verify the original CSV recorder was running while GPS was enabled. A GPS connection alone does not guarantee an active recording. |
| Help file is missing | Use the full publish folder with Docs/Data/ReferenceData, not only the EXE. |

For settings/layout problems, close the application and back up `%LOCALAPPDATA%/OBDWorkbench` before changing saved files. Reinstalling Workbench source does not reset those saved settings. Do not include that entire directory in a public issue without reviewing its logs and GPS location history.
