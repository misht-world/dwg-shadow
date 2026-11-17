# Changelog

All notable changes to this project are documented here.
The project follows [Semantic Versioning](https://semver.org/); the version is set once in `src/DwgShadow/DwgShadow.csproj`.

## [1.1.1] — 2026-11-17

### Fixed
- **AutoCAD could crash** when a number field in the dialog was cleared or got a non-number (e.g. Backspace in "Floors"). The field is now just highlighted as an error; as a safety net, any error from the plug-in dialogs is logged instead of reaching AutoCAD.
- "Pick in drawing" took existing shadow outlines inside a selection window for buildings. Objects created by DwgShadow are now ignored as sources.
- The combined zone of a shadow series was created on a layer with colour 7 (dark fill). A new zone layer now copies colour and transparency from the shadow layer.

### Added
- `tests/DialogTests`: opens the real dialogs and types invalid input into every number field.

## [1.1.0] — 2026-11-17

### Changed
- Shadows are drawn below the buildings: after every build and rebuild, shadow hatches and outlines are placed right below the lowest object on the building layers (hatches under outlines), so shadows never cover building contours or fills. Objects below the buildings (underlays, base maps) stay below the shadows. The option is now called "Shadows below buildings" and applies to outlines too.
- The installers stop before touching any files when AutoCAD is running (AutoCAD keeps the plug-in files locked).

### Added
- `demo/demo-site2.dwg` — a realistic demo block (perimeter blocks with courtyards, U-shaped blocks, a round tower).
- `tools/DemoVideo`: reads a site exported from a drawing (`export-site.lsp`), three visual styles, still frames.

## [1.0.0] — 2026-11-16

First release. A rewrite of the old Python/COM script (`old/`) as an in-process .NET plug-in for AutoCAD 2021–2024.

### Added
- `SHD` dialog: building shadows from closed contours, with a live preview (plan and section).
- `SHDSERIES` dialog: shadows for several times of a day, one layer per time, time labels, combined shadow zone.
- `-SHD`, `-SHDSERIES`: the same without a dialog, using the last saved settings.
- Sun set manually (direction and altitude) or calculated from latitude, longitude, date and time (NOAA algorithm, offline). Optional north angle.
- Low-sun safety limit: no shadow is built when the sun is lower than the limit.
- Buildings from a whole layer or picked in the drawing; nested contours as courtyards; a nested contour with its own label as a separate building part.
- Heights: one for all buildings or from TEXT/MTEXT labels inside contours (floors or metres).
- Polylines with arcs and circles.
- Safe rebuilding: each shadow object stores its source contours and the height and sun used. Rebuilding picked buildings replaces only their shadows; neighbours merged with them keep their own height and sun.
- Russian and English UI and messages; compact dialog with collapsible sections; settings remembered between sessions.
- Errors go to the command line and `%APPDATA%\DwgShadow\log.txt` instead of crashing AutoCAD; one undo reverts a whole build.
- Autoload bundle, installer scripts and a release archive.

[1.1.1]: https://github.com/misht-world/dwg-shadow/releases/tag/v1.1.1
[1.1.0]: https://github.com/misht-world/dwg-shadow/releases/tag/v1.1.0
[1.0.0]: https://github.com/misht-world/dwg-shadow/releases/tag/v1.0.0
