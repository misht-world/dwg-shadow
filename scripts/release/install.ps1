# Installs the prebuilt DwgShadow.bundle (next to this script) for the current user.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'DwgShadow.bundle'
if (-not (Test-Path $src)) { throw "DwgShadow.bundle not found next to this script: $src" }

if (Get-Process acad -ErrorAction SilentlyContinue) {
    throw 'AutoCAD is running and keeps the plug-in files locked. Close AutoCAD and run the installer again.'
}

$plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $plugins 'DwgShadow.bundle'
New-Item -ItemType Directory -Force $plugins | Out-Null
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $src $target -Recurse

# Files from a downloaded archive are marked as coming from the internet; .NET refuses to load such DLLs.
Get-ChildItem $target -Recurse -File | Unblock-File

Write-Host "DwgShadow installed: $target"
Write-Host 'Start AutoCAD and type SHD (shadows) or SHDSERIES (shadow series).'
Write-Host 'DwgShadow установлен. Запустите AutoCAD и введите SHD (тени) или SHDSERIES (серия теней).'
