# LCARS-inspired optional interface

In Preferences > General, enable **LCARS-inspired interface skin** and Save. The main window changes immediately; disable it to return to the compact classic layout. Live and replay settings persist independently, with replay copying the live settings on its first launch.

The LCARS mode uses a multi-color left elbow and navigation rail, a short upper console area, segmented framing, large active-page heading, near-black surfaces and pastel control blocks. The upper **LIVE BUS TRAFFIC** monitor shows two recent actual hexadecimal frames available from the Workbench console, ELM response path, or recorded replay samples. It never sends requests or invents bytes. Direction is labeled only when known. The Console page retains the full transcript. If a transport supplies no raw hexadecimal text, the monitor says it is waiting.

At 1366×768, the shorter masthead and compact replay footer leave more height for the active page. Live Data switches to a dedicated LCARS table column with large numeric values, ECU and update details, filtering and existing pin/context actions. Dashboard uses taller gauge faces with segmented outer arcs and a lower colored scale band; configured warning arcs and live values remain in the gauge. Trends has a signal strip above a larger chart, an LCARS settings rail, and playback controls. DTCs, Modules, PIDs and Console share the navigation, framing and table palette. Settings has a matching header. The branded connection splash is unchanged.

The CONNECTION button opens the existing transport controls. Diagnostic requests, polling, DTC operations, CSV capture and replay data are unchanged. Minimum width in LCARS mode is 950 pixels; a maximized 1366×768 desktop is the intended compact design target.

Validation here: all XAML parsed as well-formed XML; LCARS resource and event references checked; ZIP integrity passed. A Windows WPF compile and rendered interaction check remain necessary before release.

## r18 visual pass

The compact LCARS masthead combines branding, vehicle/protocol status and recent real hex traffic. The left navigation and active section headings use broad pastel bands and LCARS labels. Live Data has larger table readings plus connection, pinned readings and session panels. Gauges use taller tiles and stronger colored caps. Trends moves its legend above the chart and adds appearance, chart, connection and logging controls in the right rail. The classic skin remains available from Preferences.

## r19 layout refinement

At 1366×768, LCARS starts the gauge page in two columns so four gauges form a readable 2×2 board. A user can change the grid selector for the current LCARS window; the saved classic grid count is restored when switching skins. Live numeric gauge values use their configured accent color. The trend plot now stretches to the viewport height while retaining scrolling for stacked charts. The active page header cycles the LCARS pastel accents.

## r20 compact gauge board

Windows replay screenshots exposed two issues at 1366×768: the second row of gauges was clipped, and the numeric values crossed the lower scale bands. LCARS gauge tiles now use a shorter face, header and scale band; the value sits inside the semicircle, without a needle across it, and tick labels are simplified. The dashboard and trend instruction lines are hidden in LCARS to give the active display more vertical room. The classic gauge face, needle, grid preference and guidance remain unchanged.

## r21 concept composition pass

Trends now embeds a live-value strip beneath the plot, bound to the existing trend series and their latest values, matching the concept’s chart plus readout composition. Live Data uses a wider right rail, stronger amber numeric hierarchy, and a per-signal color capsule while keeping signal color identity. The selected row and headers remain interactive. LCARS remains optional in Preferences; the classic rail width and styles are restored when disabled.

## r22 reference-specific navigation and settings

The LCARS masthead is now a single row of vehicle, protocol, connection, real hex traffic, and five page controls. The sidebar buttons have individually rounded boundaries, black spacing and a full selection outline. The Trends settings pane has a large condensed title, LCARS/classic preview choices (classic opens Preferences), accent swatches, and grouped appearance, charts, units, connection, logging and other settings entry points. All links use existing application actions; the optional preference still governs the skin. At 1366×768 the layout allocates 420 pixels to the top page controls.
