# Changelog

All notable changes to this project are documented here.
The project follows [Semantic Versioning](https://semver.org/); the version is set once in `src/DwgShadow/DwgShadow.csproj`.

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

[1.0.0]: https://github.com/misht-world/dwg-shadow/releases/tag/v1.0.0
