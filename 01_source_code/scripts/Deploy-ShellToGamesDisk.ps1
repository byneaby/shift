<#
.SYNOPSIS
  Copies a published Shell package (extracted on C:) onto the Games disk D: for CCBoot clients.
  Intended to run from a scheduled task in a quiet window: writing D: while clients are booted
  can break already running Shells.

.EXAMPLE
  .\scripts\Deploy-ShellToGamesDisk.ps1 -Version 0.8.4
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$Version,

  [string]$StagingRoot = "C:\ShiftClub\Server\data\shell-staging",
  [string[]]$Targets = @("D:\Apps\ShiftClub\Shell", "D:\01 SHIFT\Shell"),
  [string]$BackupRoot = "C:\ShiftClub\Server\_backup",
  [string]$LogFile = "C:\ShiftClub\Server\logs\shell-d-deploy.log"
)

$ErrorActionPreference = "Stop"

function Write-Log([string]$msg) {
  $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $msg
  New-Item -ItemType Directory -Force -Path (Split-Path $LogFile) | Out-Null
  Add-Content -Path $LogFile -Value $line -Encoding UTF8
  Write-Host $line
}

try {
  $source = Join-Path $StagingRoot $Version
  $sourceExe = Join-Path $source "ShiftClub.Client.Shell.exe"
  if (-not (Test-Path $sourceExe)) { throw "Staging missing: $sourceExe" }
  $sourceVersion = (Get-Item $sourceExe).VersionInfo.ProductVersion
  Write-Log "Start: deploy Shell $Version (staging exe $sourceVersion)"

  $primary = $Targets[0]
  if (Test-Path $primary) {
    $backup = Join-Path $BackupRoot ("shell-d-{0:yyyyMMdd-HHmmss}" -f (Get-Date))
    & robocopy $primary $backup /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backup robocopy failed (code $LASTEXITCODE)" }
    Write-Log "Backup: $primary -> $backup"
  }

  foreach ($dir in $Targets) {
    if (-not $dir) { continue }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    # No /MIR: keep anything extra that lives next to Shell on the Games disk.
    & robocopy $source $dir /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    $code = $LASTEXITCODE
    if ($code -ge 8) { throw "robocopy -> $dir failed (code $code)" }

    @{ Server = @{ BaseUrl = "http://192.168.1.250:5080" } } |
      ConvertTo-Json | Set-Content (Join-Path $dir "appsettings.json") -Encoding UTF8
    Get-ChildItem $dir -Filter "*.pdb" -Recurse -Force -EA SilentlyContinue | Remove-Item -Force -EA SilentlyContinue

    $deployed = (Get-Item (Join-Path $dir "ShiftClub.Client.Shell.exe")).VersionInfo.ProductVersion
    if ($deployed -ne $sourceVersion) { throw "Version check failed in ${dir}: $deployed (expected $sourceVersion)" }
    Write-Log "OK: $dir -> $deployed (robocopy code $code)"
  }

  Write-Log "Done: Shell $Version on Games disk"
  exit 0
} catch {
  Write-Log "FAILED: $($_.Exception.Message)"
  exit 1
}
