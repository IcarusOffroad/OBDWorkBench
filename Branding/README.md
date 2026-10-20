# Icarus application icons

The feather/circuit/gear motif follows the existing splash artwork and code-native IcarusMark. Copper (#D99A6C), ivory (#F3EEE3) and charcoal keep the family consistent. Workbench uses gears; GPS uses a compass/position pointer. Details are simplified for taskbar sizes; there is no tiny text.

- IcarusWorkbench.svg / IcarusGPS.svg: editable vector masters.
- Matching PNG: 512-pixel preview/export.
- ICO: 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel frames with transparency.
- GPS offline/acquiring/fix variants add a small gray/amber/green tray badge while retaining the application mark.
- IconPreview.png: side-by-side small-size inspection sheet.

The application embeds its own ICO; the GPS package includes independent Assets copies so it can be built/copied on its own. The generation script is `generate_icons.py` (Python 3 + Pillow and Inkscape). Run it from anywhere to regenerate the assets and consumer copies. Inkscape/Pillow are developer asset-generation tools, not build/runtime dependencies. Project-authored SVG icons are covered by the root MIT license.

After rebuilding Workbench, start the new EXE. To update the installed GPS monitor, rebuild its own package and use its update/install script. Windows taskbar shortcuts can retain old icon-cache entries; unpin the old shortcut and pin the new EXE before judging the artwork.
