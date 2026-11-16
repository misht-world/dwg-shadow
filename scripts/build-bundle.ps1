# Builds the plug-in and the dist\DwgShadow.bundle package (no installation).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\DwgShadow\DwgShadow.csproj'

dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$bin = Join-Path $root 'src\DwgShadow\bin\Release'
$bundle = Join-Path $root 'dist\DwgShadow.bundle'
$contents = Join-Path $bundle 'Contents\Win64'

if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force $contents | Out-Null
Copy-Item (Join-Path $root 'bundle\PackageContents.xml') $bundle
Copy-Item (Join-Path $bin 'DwgShadow.dll'), (Join-Path $bin 'Clipper2Lib.dll') $contents
Write-Host "Bundle built: $bundle"
