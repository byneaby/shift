#Requires -Version 5.1
<#
.SYNOPSIS
  Bake Steam/Epic/Riot/Battle.net registry ACL + InstallPath into CCBoot Windows VHDs (offline).
  This is the durable fix for "Steam registry path is currently not writable" on diskless PCs.
#>
param(
  [string[]]$VhdPaths = @(
    'E:\WINDOWS 11 25H2RU-STANDART.vhd',
    'E:\WINDOWS 11 25H2RU-24.02.2026.vhd'
  ),
  [string]$SteamPath = 'F:\Steam'
)

$ErrorActionPreference = 'Stop'
$HiveName = 'OFFIMG_SOFT_SHIFT'

function Get-FreeDriveLetter {
  $used = @((Get-PSDrive -PSProvider FileSystem).Name)
  foreach ($code in 90..71) {
    $L = [string][char]$code
    if ($used -notcontains $L) { return $L }
  }
  throw 'No free drive letter'
}

function Grant-UsersFull([Microsoft.Win32.RegistryKey]$Key) {
  $acl = $Key.GetAccessControl()
  $inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
             [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
  foreach ($sid in @(
      [System.Security.Principal.WellKnownSidType]::BuiltinUsersSid,
      [System.Security.Principal.WellKnownSidType]::AuthenticatedUserSid
    )) {
    $id = New-Object System.Security.Principal.SecurityIdentifier($sid, $null)
    $rule = New-Object System.Security.AccessControl.RegistryAccessRule(
      $id,
      [System.Security.AccessControl.RegistryRights]::FullControl,
      $inherit,
      [System.Security.AccessControl.PropagationFlags]::None,
      [System.Security.AccessControl.AccessControlType]::Allow)
    $acl.SetAccessRule($rule)
  }
  $Key.SetAccessControl($acl)
}

function Apply-OfflineHive([string]$DriveLetter, [string]$SteamPath) {
  $soft = "${DriveLetter}:\Windows\System32\config\SOFTWARE"
  if (-not (Test-Path -LiteralPath $soft)) { throw "SOFTWARE hive missing: $soft" }

  # unload leftover
  cmd /c "reg unload HKLM\$HiveName >nul 2>&1"
  $load = cmd /c "reg load HKLM\$HiveName `"$soft`""
  if ($LASTEXITCODE -ne 0) { throw "reg load failed: $load" }

  try {
    $trees = @(
      'WOW6432Node\Valve',
      'WOW6432Node\Valve\Steam',
      'Valve',
      'Valve\Steam',
      'WOW6432Node\Epic Games',
      'WOW6432Node\Epic Games\EpicGamesLauncher',
      'Epic Games',
      'Epic Games\EpicGamesLauncher',
      'WOW6432Node\Riot Games',
      'Riot Games',
      'WOW6432Node\Blizzard Entertainment',
      'WOW6432Node\Blizzard Entertainment\Battle.net',
      'Blizzard Entertainment',
      'WOW6432Node\Electronic Arts',
      'Electronic Arts',
      'WOW6432Node\Ubisoft',
      'WOW6432Node\Rockstar Games',
      'Classes\steam',
      'Classes\com.epicgames.launcher',
      'Classes\riotclient',
      'Classes\battlenet'
    )

    $base = [Microsoft.Win32.Registry]::LocalMachine
    foreach ($rel in $trees) {
      $path = "$HiveName\$rel"
      $key = $base.CreateSubKey($path, $true)
      if ($null -eq $key) { Write-Warning "skip $path"; continue }
      try {
        Grant-UsersFull $key
        Write-Host "  ACL $rel"
      }
      finally { $key.Close() }
    }

    $steam = $SteamPath.TrimEnd('\')
    foreach ($p in @("$HiveName\WOW6432Node\Valve\Steam", "$HiveName\Valve\Steam")) {
      $k = $base.CreateSubKey($p, $true)
      $k.SetValue('InstallPath', $steam, [Microsoft.Win32.RegistryValueKind]::String)
      $k.Close()
    }
    Write-Host "  InstallPath=$steam"

    # protocol handlers
    $steamExe = Join-Path $steam 'steam.exe'
    $k = $base.CreateSubKey("$HiveName\Classes\steam\Shell\Open\Command", $true)
    $k.SetValue('', "`"$steamExe`" -- `"%1`"")
    $k.Close()
    $k = $base.CreateSubKey("$HiveName\Classes\steam", $true)
    $k.SetValue('', 'URL:steam protocol')
    $k.SetValue('URL Protocol', '')
    $k.Close()

    $epic = 'F:\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe'
    $k = $base.CreateSubKey("$HiveName\Classes\com.epicgames.launcher\shell\open\command", $true)
    $k.SetValue('', "`"$epic`" `"%1`"")
    $k.Close()

    $riot = 'F:\Riot Games\Riot Client\RiotClientServices.exe'
    $k = $base.CreateSubKey("$HiveName\Classes\riotclient\shell\open\command", $true)
    $k.SetValue('', "`"$riot`" --app-command=`"%1`"")
    $k.Close()

    $bn = 'F:\Battle.net\Battle.net.exe'
    $k = $base.CreateSubKey("$HiveName\Classes\battlenet\shell\open\command", $true)
    $k.SetValue('', "`"$bn`" --uri=`"%1`"")
    $k.Close()

    # Startup helper (belt): runs fix again if image ACL ever regresses
    $startup = Join-Path $MountRoot 'ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp'
    if (Test-Path -LiteralPath $startup) {
      $cmd = Join-Path $startup 'ShiftClub-LauncherFix.cmd'
      @(
        '@echo off'
        'if exist "D:\Apps\ShiftClub\LauncherFix\Fix-LauncherRegistry.ps1" ('
        '  powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "D:\Apps\ShiftClub\LauncherFix\Fix-LauncherRegistry.ps1"'
        ')'
      ) | Set-Content -LiteralPath $cmd -Encoding ASCII
      Write-Host "  Startup: $cmd"
    }
  }
  finally {
    [gc]::Collect()
    [gc]::WaitForPendingFinalizers()
    Start-Sleep -Seconds 1
    $unload = & reg.exe unload "HKLM\$HiveName" 2>&1
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "reg unload: $unload (close handles / retry)"
      Start-Sleep 2
      & reg.exe unload "HKLM\$HiveName" 2>&1 | Out-Null
    }
  }
}

foreach ($vhd in $VhdPaths) {
  Write-Host "==== $vhd ===="
  if (-not (Test-Path -LiteralPath $vhd)) {
    Write-Warning "missing VHD"
    continue
  }

  $mounted = $false
  $imagePath = $vhd
  try {
    # Dismount if already attached
    try {
      $existing = Get-DiskImage -ImagePath $vhd -ErrorAction SilentlyContinue
      if ($existing -and $existing.Attached) {
        Write-Host 'Already attached — using existing mount'
      }
      else {
        Mount-DiskImage -ImagePath $vhd -Access ReadWrite | Out-Null
        $mounted = $true
        Start-Sleep 2
      }
    }
    catch {
      throw "Mount failed (image may be locked by CCBoot/SuperClient): $($_.Exception.Message)"
    }

    $disk = Get-DiskImage -ImagePath $vhd | Get-Disk
    $part = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.Size -gt 15GB } | Sort-Object Size -Descending | Select-Object -First 1
    if (-not $part) { throw 'Windows partition not found' }

    $letter = $part.DriveLetter
    if (-not $letter) {
      $letter = Get-FreeDriveLetter
      Set-Partition -DiskNumber $part.DiskNumber -PartitionNumber $part.PartitionNumber -NewDriveLetter $letter
      Start-Sleep 1
    }
    $root = "${letter}:\"
    Write-Host "Mounted ${letter}:"
    if (-not (Test-Path (Join-Path $root 'Windows'))) { throw "Not a Windows volume: $root" }

    Apply-OfflineHive -MountRoot $root.TrimEnd('\') -SteamPath $SteamPath
    Write-Host 'OK baked into image'
  }
  catch {
    Write-Warning $_.Exception.Message
  }
  finally {
    if ($mounted) {
      try {
        Dismount-DiskImage -ImagePath $vhd -ErrorAction Stop | Out-Null
        Write-Host 'Dismounted'
      }
      catch {
        Write-Warning "Dismount: $($_.Exception.Message)"
      }
    }
  }
}

Write-Host 'Done. Reboot client PCs (or Disable/Enable image writeback) to load new registry.'
