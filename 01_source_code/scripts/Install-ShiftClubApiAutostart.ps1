# Install/repair ShiftClubApi boot autostart + health watchdog.
# Run as Administrator on the club server.
$ErrorActionPreference = 'Stop'

$serverDir = 'C:\ShiftClub\Server'
$repoScript = Join-Path $PSScriptRoot 'Start-ShiftClubApi.ps1'
$repoWatch = Join-Path $PSScriptRoot 'Watch-ShiftClubApi.ps1'
$destScript = Join-Path $serverDir 'Start-ShiftClubApi.ps1'
$destWatch = Join-Path $serverDir 'Watch-ShiftClubApi.ps1'

if (-not (Test-Path $serverDir)) {
  throw "Server dir missing: $serverDir"
}

$utf8Bom = New-Object System.Text.UTF8Encoding $true
foreach ($pair in @(@($repoScript, $destScript), @($repoWatch, $destWatch))) {
  $text = [IO.File]::ReadAllText($pair[0])
  $text = $text -replace [char]0x2014, '-' -replace [char]0x2013, '-'
  [IO.File]::WriteAllText($pair[1], $text, $utf8Bom)
}
Write-Host "Copied start + watchdog scripts to $serverDir (UTF-8 BOM)"

# --- Main API task (boot) ---
$action = New-ScheduledTaskAction `
  -Execute 'powershell.exe' `
  -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\ShiftClub\Server\Start-ShiftClubApi.ps1"' `
  -WorkingDirectory $serverDir

# Delay so PostgreSQL service can bind before first connect attempts
$trigger = New-ScheduledTaskTrigger -AtStartup
$trigger.Delay = 'PT45S'

$settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -StartWhenAvailable `
  -ExecutionTimeLimit ([TimeSpan]::Zero) `
  -RestartCount 999 `
  -RestartInterval (New-TimeSpan -Minutes 1) `
  -MultipleInstances IgnoreNew

# Critical: do NOT stop the long-running API when the machine is idle
$settings.StopIfGoingOnBatteries = $false
$settings.DisallowStartIfOnBatteries = $false
$settings.IdleSettings.StopOnIdleEnd = $false
$settings.IdleSettings.RestartOnIdle = $false
$settings.RunOnlyIfNetworkAvailable = $false

$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask `
  -TaskName 'ShiftClubApi' `
  -Action $action `
  -Trigger $trigger `
  -Settings $settings `
  -Principal $principal `
  -Force | Out-Null

Write-Host 'Registered task ShiftClubApi (AtStartup +45s, no idle stop, restart on failure)'

# --- Watchdog every 2 minutes ---
$watchAction = New-ScheduledTaskAction `
  -Execute 'powershell.exe' `
  -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\ShiftClub\Server\Watch-ShiftClubApi.ps1"' `
  -WorkingDirectory $serverDir

$watchTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).Date -RepetitionInterval (New-TimeSpan -Minutes 2) -RepetitionDuration (New-TimeSpan -Days 3650)
$watchSettings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -StartWhenAvailable `
  -ExecutionTimeLimit (New-TimeSpan -Minutes 2) `
  -MultipleInstances IgnoreNew
$watchSettings.IdleSettings.StopOnIdleEnd = $false
$watchSettings.DisallowStartIfOnBatteries = $false

Register-ScheduledTask `
  -TaskName 'ShiftClubApiWatchdog' `
  -Action $watchAction `
  -Trigger $watchTrigger `
  -Settings $watchSettings `
  -Principal $principal `
  -Force | Out-Null

Write-Host 'Registered task ShiftClubApiWatchdog (every 2 min)'

# Ensure PostgreSQL auto-start
$pg = Get-Service -Name 'postgresql-x64-18' -ErrorAction SilentlyContinue
if ($pg) {
  if ($pg.StartType -ne 'Automatic') {
    Set-Service -Name $pg.Name -StartupType Automatic
    Write-Host "Set $($pg.Name) StartupType=Automatic"
  } else {
    Write-Host "$($pg.Name) already Automatic ($($pg.Status))"
  }
} else {
  Write-Warning 'postgresql-x64-18 service not found'
}

# Ensure API is running now
$healthOk = $false
try {
  $r = Invoke-WebRequest 'http://127.0.0.1:5080/health' -UseBasicParsing -TimeoutSec 3
  $healthOk = ($r.StatusCode -eq 200)
} catch { $healthOk = $false }

if (-not $healthOk) {
  Write-Host 'API not healthy - starting ShiftClubApi...'
  Start-ScheduledTask -TaskName 'ShiftClubApi'
  Start-Sleep -Seconds 6
}

try {
  $r = Invoke-WebRequest 'http://127.0.0.1:5080/health' -UseBasicParsing -TimeoutSec 5
  Write-Host "Health: $($r.StatusCode) $($r.Content)"
} catch {
  Write-Warning "Health check failed: $($_.Exception.Message)"
}

Get-ScheduledTask -TaskName 'ShiftClubApi','ShiftClubApiWatchdog' |
  Select-Object TaskName, State |
  Format-Table -AutoSize
