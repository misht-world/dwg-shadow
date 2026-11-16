# Builds the plug-in, the dist\DwgShadow.bundle package and the release archive
# dist\DwgShadow-v<version>.zip. The version comes from <Version> in DwgShadow.csproj.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\DwgShadow\DwgShadow.csproj'

$version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version in DwgShadow.csproj must be MAJOR.MINOR.PATCH, got '$version'" }

dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$bin = Join-Path $root 'src\DwgShadow\bin\Release'
$dist = Join-Path $root 'dist'
$bundle = Join-Path $dist 'DwgShadow.bundle'
$contents = Join-Path $bundle 'Contents\Win64'

if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force $contents | Out-Null

# Manifest with the project version stamped in
$manifest = Get-Content (Join-Path $root 'bundle\PackageContents.xml') -Raw -Encoding UTF8
$manifest = $manifest -replace 'AppVersion="[^"]*"', "AppVersion=`"$version`""
[IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))
Copy-Item (Join-Path $bin 'DwgShadow.dll'), (Join-Path $bin 'Clipper2Lib.dll') $contents

# Release archive: bundle + installer scripts + readme + license
$stage = Join-Path $dist "DwgShadow-v$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $bundle $stage -Recurse
Copy-Item (Join-Path $PSScriptRoot 'release\*') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage
$zip = Join-Path $dist "DwgShadow-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

Write-Host "Version: $version"
Write-Host "Bundle:  $bundle"
Write-Host "Release: $zip"
