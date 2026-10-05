$ErrorActionPreference = 'Continue'
$token = Get-Content "$env:TEMP\sc-staff-token.txt" -Raw
$h = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }
$report = [System.Collections.Generic.List[string]]::new()
function Log([string]$m) { $report.Add("$(Get-Date -Format 'HH:mm:ss') $m"); Write-Host $m }

function Ping-Ok([string]$ip) {
  if (-not $ip) { return $false }
  & ping.exe -n 1 -w 400 $ip 2>$null | Out-Null
  return ($LASTEXITCODE -eq 0)
}
function Send-Cmd([string]$id, [string]$type) {
  $body = @{ type = $type; payloadJson = $null; idempotencyKey = [guid]::NewGuid().ToString() } | ConvertTo-Json -Compress
  try {
    return Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/computers/$id/commands" -Method POST -Headers $h -Body $body
  } catch {
    return [PSCustomObject]@{ error = $_.Exception.Message }
  }
}
function Send-Wake([string]$id) {
  try {
    return Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/computers/$id/wake" -Method POST -Headers $h
  } catch {
    return [PSCustomObject]@{ error = $_.Exception.Message }
  }
}

$computers = (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/computers' -Headers $h).data
$up = @($computers | Where-Object { Ping-Ok $_.ipAddress })
Log "START UP=$($up.Count): $($up.displayName -join ',')"
Log "Free G=$([math]::Round((Get-PSDrive G).Free/1GB,1)) I=$([math]::Round((Get-PSDrive I).Free/1GB,1)) H=$([math]::Round((Get-PSDrive H).Free/1GB,1))"

# Phase Restart
Log "=== RESTART $($up.Count) ==="
foreach ($c in $up) {
  $r = Send-Cmd $c.id 'Restart'
  Log "Restart $($c.displayName): $(if ($r.error) { $r.error } else { 'ok' })"
}

Log "Wait drop..."
$deadline = (Get-Date).AddSeconds(100)
do {
  Start-Sleep 5
  $still = @($up | Where-Object { Ping-Ok $_.ipAddress }).Count
  Log "stillUp=$still"
} while ($still -gt 2 -and (Get-Date) -lt $deadline)

Log "Wait back max 300s..."
$deadline = (Get-Date).AddSeconds(300)
$back = @{}
do {
  Start-Sleep 8
  foreach ($c in $up) {
    if (-not $back.ContainsKey($c.displayName) -and (Ping-Ok $c.ipAddress)) {
      $back[$c.displayName] = Get-Date
      Log "BACK $($c.displayName)"
    }
  }
  Log "back=$($back.Count)/$($up.Count)"
} while ($back.Count -lt $up.Count -and (Get-Date) -lt $deadline)
Log "RESTART_RESULT $($back.Count)/$($up.Count)"

# Phase Shutdown then WoL for previously up
Start-Sleep 10
$computers = (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/computers' -Headers $h).data
$up2 = @($computers | Where-Object { $_.displayName -in $up.displayName -and (Ping-Ok $_.ipAddress) })
Log "=== SHUTDOWN $($up2.Count) ==="
foreach ($c in $up2) {
  $r = Send-Cmd $c.id 'Shutdown'
  Log "Shutdown $($c.displayName): $(if ($r.error) { $r.error } else { 'ok' })"
}

$deadline = (Get-Date).AddSeconds(120)
do {
  Start-Sleep 5
  $still = @($up | Where-Object { Ping-Ok $_.ipAddress }).Count
  Log "afterShutdown stillUp=$still"
} while ($still -gt 0 -and (Get-Date) -lt $deadline)

Start-Sleep 15
Log "=== WoL previously-online set ==="
foreach ($c in $up) {
  if (-not $c.macAddress) { Log "SKIP no MAC $($c.displayName)"; continue }
  $r = Send-Wake $c.id
  Log "WoL $($c.displayName): $(if ($r.error) { $r.error } else { 'ok' })"
}

Log "Wait WoL back max 360s..."
$deadline = (Get-Date).AddSeconds(360)
$back2 = @{}
do {
  Start-Sleep 10
  foreach ($c in $up) {
    if (-not $back2.ContainsKey($c.displayName) -and (Ping-Ok $c.ipAddress)) {
      $back2[$c.displayName] = Get-Date
      Log "WOL_BACK $($c.displayName)"
    }
  }
  Log "wolBack=$($back2.Count)/$($up.Count)"
} while ($back2.Count -lt $up.Count -and (Get-Date) -lt $deadline)
Log "WOL_PREV_RESULT $($back2.Count)/$($up.Count)"

# Phase: WoL remaining offline with MAC
$computers = (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/computers' -Headers $h).data
$offline = @($computers | Where-Object {
    $_.macAddress -and $_.displayName -notin $up.displayName -and -not (Ping-Ok $_.ipAddress)
  })
Log "=== WoL OFFLINE leftover $($offline.Count): $($offline.displayName -join ',') ==="
foreach ($c in $offline) {
  $r = Send-Wake $c.id
  Log "WoL-off $($c.displayName): $(if ($r.error) { $r.error } else { 'ok' })"
}

Log "Wait offline wave max 420s..."
$deadline = (Get-Date).AddSeconds(420)
$back3 = @{}
do {
  Start-Sleep 12
  foreach ($c in $offline) {
    if (-not $back3.ContainsKey($c.displayName) -and (Ping-Ok $c.ipAddress)) {
      $back3[$c.displayName] = Get-Date
      Log "OFF_BACK $($c.displayName)"
    }
  }
  # also count previously online
  $allUp = @($computers | Where-Object { Ping-Ok $_.ipAddress }).Count
  Log "offBack=$($back3.Count)/$($offline.Count) totalPingUp=$allUp"
} while ($back3.Count -lt $offline.Count -and (Get-Date) -lt $deadline)

Log "WOL_OFFLINE_RESULT $($back3.Count)/$($offline.Count)"

# Final inventory
$computers = (Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/computers' -Headers $h).data
$final = foreach ($c in $computers) {
  [PSCustomObject]@{
    PC = $c.displayName
    Ping = Ping-Ok $c.ipAddress
    Status = $c.status
    MAC = $c.macAddress
    IP = $c.ipAddress
  }
}
Log "=== FINAL ==="
$final | Sort-Object { [int]($_.PC -replace '\D', '0') } | ForEach-Object {
  Log ("FINAL {0} ping={1} status={2} ip={3}" -f $_.PC, $_.Ping, $_.Status, $_.IP)
}
Log "FINAL_UP $((@($final | Where-Object Ping)).Count)/$($final.Count)"
Log "Free G=$([math]::Round((Get-PSDrive G).Free/1GB,1)) I=$([math]::Round((Get-PSDrive I).Free/1GB,1)) H=$([math]::Round((Get-PSDrive H).Free/1GB,1))"

# Writeback in-use large files
Log "=== LARGE WRITEBACK >100MB ==="
foreach ($letter in @('G', 'I')) {
  Get-ChildItem "${letter}:\" -Filter 'PC*-*.vhd*' -File -EA SilentlyContinue |
    Where-Object { $_.Length -gt 100MB } |
    Sort-Object Length -Descending |
    ForEach-Object { Log ("WB {0} {1} MB={2} write={3}" -f $letter, $_.Name, [math]::Round($_.Length / 1MB, 1), $_.LastWriteTime) }
}

$reportPath = 'C:\ShiftClub\Project\scripts\_club-resilience-report.txt'
$report | Set-Content $reportPath -Encoding UTF8
Log "Report saved $reportPath"
