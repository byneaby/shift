<#
.SYNOPSIS
  Builds a single EXE installer for SHIFT Club client (Shell + Updater).

.EXAMPLE
  .\scripts\Build-ClientInstaller.ps1

.EXAMPLE
  .\scripts\Build-ClientInstaller.ps1 -Version 0.6.27 -ServerUrl http://192.168.1.200:5080
#>
param(
  [string]$Version = "",
  [string]$ServerUrl = "http://192.168.1.250:5080",
  [string]$RepoRoot = "",
  [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$env:PATH = "D:\Dev\dotnet;D:\Dev\Git\cmd;$env:PATH"
$env:DOTNET_ROOT = "D:\Dev\dotnet"

if (-not $RepoRoot) {
  $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$versionFile = Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Shell\ClientVersionInfo.cs"
if (-not $Version) {
  $raw = Get-Content $versionFile -Raw
  if ($raw -match 'Version = "([^"]+)"') { $Version = $Matches[1] } else { $Version = "0.0.0" }
}

$installerProj = Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Installer\ShiftClub.Client.Installer.csproj"
$payloadDir = Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Installer\payload"
$stage = Join-Path $RepoRoot "dist\client-installer-stage\$Version"
$shellOut = Join-Path $stage "client"
$zipPath = Join-Path $payloadDir "client.zip"
$outDir = Join-Path $RepoRoot "dist\installer"
$finalExe = Join-Path $outDir "ShiftClub.Client.Setup-$Version.exe"

Write-Host "== SHIFT Club Client Installer $Version =="

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $shellOut | Out-Null
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$publishArgs = @(
  "-c", "Release",
  "-r", "win-x64",
  "-o", $shellOut
)
if ($FrameworkDependent) {
  $publishArgs += @("--self-contained", "false")
} else {
  $publishArgs += @("--self-contained", "true", "-p:PublishReadyToRun=true")
}

Write-Host "== Publish Shell (win-x64) =="
dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Shell\ShiftClub.Client.Shell.csproj") @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Shell publish failed" }

Write-Host "== Publish Updater (win-x64) =="
$updaterOut = Join-Path $stage "updater"
New-Item -ItemType Directory -Force -Path $updaterOut | Out-Null
$updaterArgs = @(
  "-c", "Release",
  "-r", "win-x64",
  "-o", $updaterOut
)
if ($FrameworkDependent) {
  $updaterArgs += @("--self-contained", "false")
} else {
  # Single-file so we only drop Updater next to Shell — never overwrite WPF DLLs
  # (console WindowsBase stub would break ShiftClub.Client.Shell.exe).
  $updaterArgs += @(
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
  )
}
dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Updater\ShiftClub.Client.Updater.csproj") @updaterArgs
if ($LASTEXITCODE -ne 0) { throw "Updater publish failed" }

# Copy ONLY Updater binaries — do not merge full runtime tree into Shell output.
$updaterNames = @(
  "ShiftClub.Client.Updater.exe",
  "ShiftClub.Client.Updater.dll",
  "ShiftClub.Client.Updater.deps.json",
  "ShiftClub.Client.Updater.runtimeconfig.json",
  "ShiftClub.Client.Updater.pdb"
)
foreach ($name in $updaterNames) {
  $src = Join-Path $updaterOut $name
  if (Test-Path $src) {
    Copy-Item $src $shellOut -Force
  }
}
$updaterExe = Join-Path $shellOut "ShiftClub.Client.Updater.exe"
if (-not (Test-Path $updaterExe)) { throw "Updater exe missing after copy: $updaterExe" }

Write-Host "== Publish Client.Service (watchdog) =="
$serviceOut = Join-Path $shellOut "service"
New-Item -ItemType Directory -Force -Path $serviceOut | Out-Null
$serviceArgs = @(
  "-c", "Release",
  "-r", "win-x64",
  "-o", $serviceOut,
  "--self-contained", "true",
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true"
)
dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Service\ShiftClub.Client.Service.csproj") @serviceArgs
if ($LASTEXITCODE -ne 0) { throw "Service publish failed" }
@{
  Server = @{ BaseUrl = $ServerUrl.TrimEnd('/') }
  Shell  = @{ ExePath = "D:\Apps\ShiftClub\Shell\ShiftClub.Client.Shell.exe" }
} | ConvertTo-Json | Set-Content -Path (Join-Path $serviceOut "appsettings.json") -Encoding UTF8

# Default server URL into packaged appsettings
$appsettings = Join-Path $shellOut "appsettings.json"
@{
  Server = @{ BaseUrl = $ServerUrl.TrimEnd('/') }
} | ConvertTo-Json | Set-Content -Path $appsettings -Encoding UTF8

Write-Host "== Pack payload =="
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($shellOut, $zipPath)
$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Payload: $zipPath ($zipMb MB)"

# Keep Installer version in sync
$proj = Get-Content $installerProj -Raw
$proj = [regex]::Replace($proj, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
$proj = [regex]::Replace($proj, '<InformationalVersion>[^<]*</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>")
Set-Content -Path $installerProj -Value $proj -Encoding UTF8

Write-Host "== Publish Setup EXE (single-file) =="
$setupOut = Join-Path $stage "setup"
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }
dotnet publish $installerProj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:Version=$Version `
  -p:InformationalVersion=$Version `
  -o $setupOut
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

$built = Join-Path $setupOut "ShiftClub.Client.Setup.exe"
if (-not (Test-Path $built)) { throw "Setup exe not found: $built" }

Copy-Item $built $finalExe -Force
Copy-Item $built (Join-Path $outDir "ShiftClub.Client.Setup.exe") -Force

$sizeMb = [math]::Round((Get-Item $finalExe).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer ready:"
Write-Host "  $finalExe"
Write-Host "  $outDir\ShiftClub.Client.Setup.exe"
Write-Host "  Size: $sizeMb MB"
Write-Host "  Default API: $ServerUrl"
Write-Host "Done."
