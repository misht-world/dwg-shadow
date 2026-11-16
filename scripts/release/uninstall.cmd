@echo off
rem Removes DwgShadow for the current user. Settings in %APPDATA%\DwgShadow are kept.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DwgShadow.bundle'; if (Test-Path $t) { Remove-Item $t -Recurse -Force; Write-Host ('Removed: ' + $t) } else { Write-Host 'DwgShadow is not installed.' }"
pause
