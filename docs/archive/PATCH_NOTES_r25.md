# r25 — splash proportions and GPS diagnostic clarity

- Splash header preserves the cropped artwork's original proportions using Uniform scaling, with height calculated from the image aspect ratio and available window height. The decorative lower background uses UniformToFill rather than stretching.
- GPS checkbox explicitly says “Enable GPS in Workbench.” Selecting a source alone does not enable reception.
- Disabled GPS diagnostics now explain how to subscribe, including that the independent service may still be running and a location fix is not required to receive diagnostics.
- Service diagnostics additionally show port-open/data-fresh/fix-valid states, satellites used, in-view counts by talker, HDOP, fix quality and acquisition time, including while no fix is available.
- GPS source selection, channels and bundled service are unchanged from r24.

For the supplied indoor screenshots: Workbench's Enable GPS checkbox is unchecked, while the GPS monitor shows the service receiving NMEA on COM6. Enable GPS in Workbench with source Icarus GPS Service to view its status and recent NMEA. Position/speed remain unavailable until valid.

Validation: Windows-target application build passes. The startup layout and enabled service subscription still need visual/physical Windows acceptance. r24's 190 passing regression checks and service tests apply to unchanged GPS data handling; this patch changes layout and diagnostic wording only.
