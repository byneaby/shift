#Requires -Version 5.1
<#
.SYNOPSIS
  SHIFT Club: Steam GameFix for diskless clients (configs survive reboot).

  What other clubs enable via iCafe "Game Save + Steam Game Fixes", adapted for
  ShiftClub Shell (games launch without iCafeMenu).

.DESCRIPTION
  1) Ensures F:\GameFixes\Steam\{APPDATA,userdata,config,ProgramData}
  2) Junctions F:\Steam\userdata + F:\Steam\config -> GameFixes (games disk)
  3) Patches C:\CCBoot\GameFix.bat so each client boot links:
       %LocalAppData%\Steam, %AppData%\Steam, %ProgramData%\Steam
       (+ all C:\Users\*\AppData\...)
       D:\Steam\userdata + D:\Steam\config

  Safe to re-run. Backs up GameFix.bat before patch.

.NOTES
  If iCafeCloud CP regenerates GameFix.bat, re-run this script OR add Steam in
  iCafe Game Fixes so the generated bat includes Steam again.
#>
param(
  [string]$GameDrive = 'D:',
  [string]$SteamRoot = 'F:\Steam',
  [string]$GameFixesSteam = 'F:\GameFixes\Steam',
  [string]$GameFixBat = 'C:\CCBoot\GameFix.bat',
  [switch]$SkipBatPatch
)

$ErrorActionPreference = 'Stop'

function Test-ReparsePoint([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path)) { return $false }
  $i = Get-Item -LiteralPath $Path -Force
  return [bool]($i.Attributes -band [IO.FileAttributes]::ReparsePoint)
}

function Ensure-Dir([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
  }
}

function Ensure-Junction([string]$Link, [string]$Target) {
  Ensure-Dir $Target
  if (Test-Path -LiteralPath $Link) {
    if (Test-ReparsePoint $Link) {
      $cur = (Get-Item -LiteralPath $Link -Force).Target
      $curStr = if ($cur -is [array]) { $cur -join ';' } else { [string]$cur }
      if ($curStr -and ($curStr -replace '/', '\').TrimEnd('\') -ieq ($Target.TrimEnd('\'))) {
        Write-Host "OK junction $Link -> $Target"
        return
      }
      cmd /c "rmdir `"$Link`"" | Out-Null
    }
    else {
      $bak = "$Link.bak-shiftclub-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
      Write-Host "Rename $Link -> $bak"
      Rename-Item -LiteralPath $Link -NewName (Split-Path $bak -Leaf)
      # Rename-Item keeps parent; fix absolute bak path
      $parent = Split-Path $Link -Parent
      $bakFull = Join-Path $parent (Split-Path $bak -Leaf)
      if ((Test-Path -LiteralPath $Link) -and -not (Test-Path -LiteralPath $bakFull)) {
        # already renamed via NewName relative to parent
      }
      # Copy remaining data into target if we renamed
      if (Test-Path -LiteralPath $bakFull) {
        & robocopy $bakFull $Target /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
      }
    }
  }
  cmd /c "mklink /D `"$Link`" `"$Target`""
  if ($LASTEXITCODE -ne 0) { throw "mklink failed: $Link -> $Target" }
  Write-Host "Created junction $Link -> $Target"
}

Write-Host '=== SHIFT Steam GameFix ==='

foreach ($d in @(
    (Join-Path $GameFixesSteam 'APPDATA\Local'),
    (Join-Path $GameFixesSteam 'APPDATA\Roaming'),
    (Join-Path $GameFixesSteam 'ProgramData'),
    (Join-Path $GameFixesSteam 'userdata'),
    (Join-Path $GameFixesSteam 'config')
  )) {
  Ensure-Dir $d
}

Ensure-Dir 'C:\iCafeCloudShare\SteamData'

# Seed GameFixes from live Steam folders if empty / first run
foreach ($pair in @(
    @{ Src = (Join-Path $SteamRoot 'userdata'); Dst = (Join-Path $GameFixesSteam 'userdata') },
    @{ Src = (Join-Path $SteamRoot 'config');   Dst = (Join-Path $GameFixesSteam 'config') }
  )) {
  if ((Test-Path -LiteralPath $pair.Src) -and -not (Test-ReparsePoint $pair.Src)) {
    Write-Host "Robocopy $($pair.Src) -> $($pair.Dst)"
    & robocopy $pair.Src $pair.Dst /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
  }
}

Ensure-Junction (Join-Path $SteamRoot 'userdata') (Join-Path $GameFixesSteam 'userdata')
Ensure-Junction (Join-Path $SteamRoot 'config') (Join-Path $GameFixesSteam 'config')

if (-not $SkipBatPatch) {
  if (-not (Test-Path -LiteralPath $GameFixBat)) {
    throw "GameFix.bat not found: $GameFixBat"
  }

  $text = Get-Content -LiteralPath $GameFixBat -Raw -Encoding UTF8
  if ($text -match 'GameFix for Steam \(SHIFT Club') {
    Write-Host 'GameFix.bat already contains SHIFT Steam block'
  }
  else {
    $bak = "$GameFixBat.bak-before-steam-shiftclub-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $GameFixBat -Destination $bak -Force
    Write-Host "Backup: $bak"

    $steamBlock = @"
rem ==== GameFix for Steam (SHIFT Club / Shell — configs survive reboot) ====
rem Keep Steam closed while linking userdata/config
tasklist | find /I "steam.exe"
if %errorlevel% equ 0 (
	taskkill /F /IM steam.exe /T
	timeout 2
)
tasklist | find /I "steamwebhelper.exe"
if %errorlevel% equ 0 (
	taskkill /F /IM steamwebhelper.exe /T
	timeout 1
)
rem AppData (volatile C: profile) -> Games disk GameFixes
call :do_mklink "%LocalAppData%" "Steam" 1 "Steam\APPDATA\Local"
call :do_mklink "%AppData%" "Steam" 1 "Steam\APPDATA\Roaming"
call :do_mklink "%ProgramData%" "Steam" 1 "Steam\ProgramData"
rem Also fix common club Windows profiles (GameFix may run as Admin/SYSTEM)
for /d %%U in ("%SystemDrive%\Users\*") do (
	if /I not "%%~nxU"=="Public" if /I not "%%~nxU"=="Default User" (
		if exist "%%U\AppData\Local\" call :do_mklink "%%U\AppData\Local" "Steam" 1 "Steam\APPDATA\Local"
		if exist "%%U\AppData\Roaming\" call :do_mklink "%%U\AppData\Roaming" "Steam" 1 "Steam\APPDATA\Roaming"
	)
)
rem Steam install on games disk: userdata + config -> GameFixes (CS2 cfg / login)
if exist "%gamedrive%\Steam\" (
	call :do_mklink "%gamedrive%\Steam" "userdata" 1 "Steam\userdata"
	call :do_mklink "%gamedrive%\Steam" "config" 1 "Steam\config"
)

"@

    if ($text -notmatch '(?m)^goto do_mklink_end\s*$') {
      throw 'Could not find "goto do_mklink_end" in GameFix.bat — abort patch'
    }

    $text2 = $text -replace '(?m)^goto do_mklink_end\s*\r?\n', ($steamBlock + "goto do_mklink_end`r`n")
    $text2 = $text2 -replace 'set "GameSave=0"', 'set "GameSave=1"'
    Set-Content -LiteralPath $GameFixBat -Value $text2 -Encoding UTF8 -NoNewline
    Write-Host "Patched $GameFixBat (GameSave=1 + Steam block)"
  }
}

Write-Host ''
Write-Host 'Done. Reboot a client PC, change CS2 settings, reboot again — settings should remain.'
Write-Host 'If iCafe regenerates GameFix.bat, re-run this script.'
