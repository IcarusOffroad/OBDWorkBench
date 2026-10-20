# r24 — GPS service source selection

Based on r23. Open Preferences → GPS connection and NMEA debug → GPS source. Choose Direct COM port or Icarus GPS Service. Choice, enabled state and COM settings save automatically; old settings default to Direct COM. The same GPS channels feed existing live cards, gauges, trends, CSV recording and replay.

Service mode reads Icarus.GPS.Data.v1 snapshots on a background subscription, without opening COM or controlling the service. It reconnects after errors and rejects stale or incompatible data. The config/debug screen shows receiver/service status and recent NMEA. Normal log records retain the service publication time and receiver GPS time in their JSON evidence; channel source remains GPS for consistent trend identity across both sources.

The complete supplied IcarusGPS v0.2.3 package is under GPSService/IcarusGPS_v0.2.4. Start with GPSService/README_WORKBENCH.md for build/install, usage, ownership and troubleshooting. Workbench itself does not require virtual COM ports. Build/install the service separately only if needed.

Physical Windows receiver/service coexistence and visual acceptance remain to be tested on the PC. See GPS_BUILD_VALIDATION_r24.txt for automated results.
