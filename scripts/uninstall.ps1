# Removes the DwgShadow plug-in (settings in %APPDATA%\DwgShadow are kept).
$target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DwgShadow.bundle'
if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
    Write-Host "Removed: $target"
} else {
    Write-Host 'The plug-in is not installed.'
}
