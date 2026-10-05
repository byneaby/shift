<#
.SYNOPSIS
  Copies ShiftClub.Client.Updater.* next to an existing Shell install.
  Use when "Обновить" fails with "Updater не найден рядом с Shell".

.EXAMPLE
  .\scripts\Bootstrap-ClientUpdater.ps1 -ShellDir "C:\ShiftClub\Client"

.EXAMPLE
  .\scripts\Bootstrap-ClientUpdater.ps1 -ShellDir "\\PC01\c$\ShiftClub\Client"
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$ShellDir,

  [string]$PackageZip = "",
  [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
  $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

if (-not $PackageZip) {
  $packages = Join-Path $RepoRoot "src\ShiftClub.Server\data\client-updates"
  $manifest = Get-Content (Join-Path $packages "manifest.json") -Raw | ConvertFrom-Json
  $PackageZip = Join-Path $packages $manifest.packageFile
}

if (-not (Test-Path $PackageZip)) { throw "Package not found: $PackageZip" }
if (-not (Test-Path $ShellDir)) { throw "Shell dir not found: $ShellDir" }

$tmp = Join-Path $env:TEMP ("ShiftClubBoot_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
  Expand-Archive -Path $PackageZip -DestinationPath $tmp -Force
  $updater = Get-ChildItem -Path $tmp -Recurse -Filter "ShiftClub.Client.Updater.exe" | Select-Object -First 1
  if (-not $updater) { throw "Updater.exe missing inside $PackageZip" }
  $srcDir = $updater.Directory.FullName
  foreach ($name in @(
      "ShiftClub.Client.Updater.exe",
      "ShiftClub.Client.Updater.dll",
      "ShiftClub.Client.Updater.runtimeconfig.json",
      "ShiftClub.Client.Updater.deps.json"
    )) {
    $src = Join-Path $srcDir $name
    if (Test-Path $src) {
      Copy-Item $src (Join-Path $ShellDir $name) -Force
      Write-Host "OK: $name"
    }
  }
}
finally {
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Updater installed into: $ShellDir"
Write-Host "Restart Shell, then send «Обновить» from the floor map."
