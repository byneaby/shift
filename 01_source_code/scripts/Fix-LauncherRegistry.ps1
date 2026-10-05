#Requires -Version 5.1
<#
.SYNOPSIS
  Fix Steam / Epic / Riot / Battle.net / EA registry write ACL + InstallPath.
  Root cause of "Steam registry path is currently not writable": Users only have ReadKey on HKLM\...\Valve.
  Run as SYSTEM/Admin (GameFix boot or ShiftClubClient service).
#>
param(
  [string]$SteamPath = 'F:\Steam',
  [string]$EpicLauncher = 'F:\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe',
  [string]$RiotClient = 'F:\Riot Games\Riot Client\RiotClientServices.exe',
  [string]$BattleNet = 'F:\Battle.net\Battle.net.exe'
)

$ErrorActionPreference = 'Continue'

function Grant-RegistryUsersFull([string]$HivePath) {
  # HivePath like SOFTWARE\WOW6432Node\Valve (under HKLM)
  try {
    $base = [Microsoft.Win32.Registry]::LocalMachine
    $key = $base.CreateSubKey($HivePath)
    if (-not $key) { throw "cannot open $HivePath" }

    $acl = $key.GetAccessControl()
    $inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
               [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $prop = [System.Security.AccessControl.PropagationFlags]::None
    $right = [System.Security.AccessControl.RegistryRights]::FullControl

    foreach ($id in @('BUILTIN\Users', 'NT AUTHORITY\Authenticated Users')) {
      $rule = New-Object System.Security.AccessControl.RegistryAccessRule(
        $id, $right, $inherit, $prop,
        [System.Security.AccessControl.AccessControlType]::Allow)
      $acl.SetAccessRule($rule)
    }
    $key.SetAccessControl($acl)
    $key.Close()
    Write-Host "ACL OK HKLM\$HivePath"
  }
  catch {
    Write-Warning "ACL fail HKLM\$HivePath : $($_.Exception.Message)"
  }
}

function Set-RegSz([Microsoft.Win32.RegistryKey]$Root, [string]$SubPath, [string]$Name, [string]$Value) {
  try {
    $k = $Root.CreateSubKey($SubPath)
    $k.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]::String)
    $k.Close()
  }
  catch {
    Write-Warning "SetValue fail $SubPath\$Name : $($_.Exception.Message)"
  }
}

# --- ensure parent trees exist + writable ---
$writableTrees = @(
  'SOFTWARE\WOW6432Node\Valve',
  'SOFTWARE\WOW6432Node\Valve\Steam',
  'SOFTWARE\Valve',
  'SOFTWARE\Valve\Steam',
  'SOFTWARE\WOW6432Node\Epic Games',
  'SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher',
  'SOFTWARE\Epic Games',
  'SOFTWARE\Epic Games\EpicGamesLauncher',
  'SOFTWARE\WOW6432Node\Riot Games',
  'SOFTWARE\Riot Games',
  'SOFTWARE\WOW6432Node\Blizzard Entertainment',
  'SOFTWARE\Blizzard Entertainment',
  'SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net',
  'SOFTWARE\WOW6432Node\Electronic Arts',
  'SOFTWARE\Electronic Arts',
  'SOFTWARE\WOW6432Node\Ubisoft',
  'SOFTWARE\WOW6432Node\Rockstar Games',
  'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 0'
)

foreach ($t in $writableTrees) {
  try { [void][Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($t) } catch {}
  Grant-RegistryUsersFull $t
}

# Also grant on classes for protocol handlers (often written at first launch)
foreach ($t in @(
  'SOFTWARE\Classes\steam',
  'SOFTWARE\Classes\com.epicgames.launcher',
  'SOFTWARE\Classes\riotclient',
  'SOFTWARE\Classes\battlenet',
  'SOFTWARE\WOW6432Node\Classes\steam'
)) {
  try { [void][Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($t) } catch {}
  Grant-RegistryUsersFull $t
}

# --- seed InstallPath / SteamPath ---
$steamNorm = $SteamPath.TrimEnd('\')
if (Test-Path -LiteralPath (Join-Path $steamNorm 'steam.exe')) {
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\WOW6432Node\Valve\Steam' 'InstallPath' $steamNorm
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\Valve\Steam' 'InstallPath' $steamNorm

  $steamSlash = ($steamNorm -replace '\\', '/').ToLowerInvariant()
  Set-RegSz ([Microsoft.Win32.Registry]::CurrentUser) 'Software\Valve\Steam' 'SteamPath' $steamSlash
  Set-RegSz ([Microsoft.Win32.Registry]::CurrentUser) 'Software\Valve\Steam' 'SteamExe' ($steamSlash + '/steam.exe')
  Write-Host "Steam InstallPath=$steamNorm"
}
else {
  Write-Warning "Steam exe missing: $steamNorm\steam.exe"
}

if (Test-Path -LiteralPath $EpicLauncher) {
  $epicRoot = 'F:\Epic Games'
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher' 'AppDataPath' 'F:\GameFixes\Epic\APPDATA\Local'
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\WOW6432Node\EpicGames\EpicGamesLauncher' 'InstallLocation' $epicRoot
  Write-Host "Epic paths seeded"
}

if (Test-Path -LiteralPath $RiotClient) {
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\WOW6432Node\Riot Games\Riot Client' 'Install Folder' 'F:\Riot Games\Riot Client'
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\Riot Games\Riot Client' 'Install Folder' 'F:\Riot Games\Riot Client'
  Write-Host "Riot paths seeded"
}

if (Test-Path -LiteralPath $BattleNet) {
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net' 'InstallPath' 'F:\Battle.net'
  Set-RegSz ([Microsoft.Win32.Registry]::LocalMachine) 'SOFTWARE\Blizzard Entertainment\Battle.net' 'InstallPath' 'F:\Battle.net'
  Write-Host "Battle.net paths seeded"
}

# Protocol handlers (match junctions D:\Steam etc.)
$steamExe = Join-Path $steamNorm 'steam.exe'
cmd /c "reg add HKCR\steam /f /ve /t REG_SZ /d `"URL:steam protocol`" >nul"
cmd /c "reg add HKCR\steam /f /v `"URL Protocol`" /t REG_SZ /d `"`" >nul"
cmd /c "reg add `"HKCR\steam\Shell\Open\Command`" /f /ve /t REG_SZ /d `"\`"$steamExe\`" -- \`"%1\`"`" >nul"

if (Test-Path -LiteralPath $EpicLauncher) {
  cmd /c "reg add HKCR\com.epicgames.launcher /f /ve /t REG_SZ /d `"Epic Games Launcher Link`" >nul"
  cmd /c "reg add HKCR\com.epicgames.launcher /f /v `"URL Protocol`" /t REG_SZ /d `"`" >nul"
  cmd /c "reg add `"HKCR\com.epicgames.launcher\shell\open\command`" /f /ve /t REG_SZ /d `"\`"$EpicLauncher\`" `"%%1`"`" >nul"
}

if (Test-Path -LiteralPath $RiotClient) {
  cmd /c "reg add HKCR\riotclient /f /ve /t REG_SZ /d `"URL:Riot Games Protocol`" >nul"
  cmd /c "reg add HKCR\riotclient /f /v `"URL Protocol`" /t REG_SZ /d `"`" >nul"
  cmd /c "reg add `"HKCR\riotclient\shell\open\command`" /f /ve /t REG_SZ /d `"\`"$RiotClient\`" --app-command=`"%%1`"`" >nul"
}

if (Test-Path -LiteralPath $BattleNet) {
  cmd /c "reg add HKCR\battlenet /f /ve /t REG_SZ /d `"URL:Blizzard Battle.net Protocol`" >nul"
  cmd /c "reg add HKCR\battlenet /f /v `"URL Protocol`" /t REG_SZ /d `"`" >nul"
  cmd /c "reg add `"HKCR\battlenet\shell\open\command`" /f /ve /t REG_SZ /d `"\`"$BattleNet\`" --uri=`"%%1`"`" >nul"
}

Write-Host 'Fix-LauncherRegistry done.'
