# Ensure SHIFT Club API is healthy; start task if down.
# Safe to run periodically from Task Scheduler. Lives next to the server files.
$ErrorActionPreference = 'Continue'
$logDir = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logFile = Join-Path $logDir 'watchdog.log'

function Write-WatchLog([string]$msg) {
  $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $msg
  Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Get-ListenPort {
  $secrets = Join-Path $PSScriptRoot 'secrets.env'
  if (-not (Test-Path $secrets)) { return 5080 }
  foreach ($line in Get-Content $secrets) {
    $text = $line.Trim()
    if ($text -match '^Kestrel__Endpoints__Http__Url=https?://[^:/]+:(\d+)\s*$') {
      return [int]$Matches[1]
    }
  }
  return 5080
}

$port = Get-ListenPort

function Test-ApiHealthy {
  try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 4
    return ($r.StatusCode -eq 200 -and ($r.Content -match 'Healthy|healthy|Degraded'))
  } catch {
    return $false
  }
}

if (Test-ApiHealthy) {
  exit 0
}

Write-WatchLog 'API unhealthy - ensuring ShiftClubApi task is running'
try {
  $task = Get-ScheduledTask -TaskName 'ShiftClubApi' -ErrorAction Stop
  if ($task.State -ne 'Running') {
    Write-WatchLog "Task state=$($task.State) - starting"
    Start-ScheduledTask -TaskName 'ShiftClubApi'
  } else {
    # Stuck Running but not answering: recycle
    Write-WatchLog 'Task Running but health fail - End + Run'
    schtasks.exe /End /TN 'ShiftClubApi' | Out-Null
    Start-Sleep -Seconds 2
    schtasks.exe /Run /TN 'ShiftClubApi' | Out-Null
  }
} catch {
  Write-WatchLog "Watchdog error: $($_.Exception.Message)"
  exit 1
}

Start-Sleep -Seconds 8
if (Test-ApiHealthy) {
  Write-WatchLog 'API recovered'
  exit 0
}
Write-WatchLog 'API still unhealthy after restart attempt'
exit 1
