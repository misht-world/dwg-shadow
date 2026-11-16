# Builds and installs the DwgShadow plug-in for the current user.
# AutoCAD 2021–2024 then loads it automatically on start.
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -NoBuild   (use the existing dist\)

param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bundleSrc = Join-Path $root 'dist\DwgShadow.bundle'

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-bundle.ps1')
}
if (-not (Test-Path $bundleSrc)) { throw "Bundle not found: $bundleSrc" }

if (Get-Process acad -ErrorAction SilentlyContinue) {
    Write-Warning 'AutoCAD is running: plug-in files may be locked. Close AutoCAD and run again if copying fails.'
}

$target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DwgShadow.bundle'
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $bundleSrc $target -Recurse
Write-Host "Installed: $target"
Write-Host 'Start AutoCAD and type SHD (shadows) or SHDSERIES (shadow series).'
