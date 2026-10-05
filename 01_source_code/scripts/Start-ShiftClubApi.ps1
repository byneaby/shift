# Start SHIFT Club API (Production) with Postgres wait + crash restart.
# Canonical install: C:\ShiftClub\Server
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$logDir = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logFile = Join-Path $logDir 'startup.log'

function Write-BootLog([string]$msg) {
  $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $msg
  Add-Content -Path $logFile -Value $line -Encoding UTF8
  Write-Host $line
}

function Test-TcpPort([string]$hostName, [int]$port, [int]$timeoutMs = 1500) {
  try {
    $client = New-Object System.Net.Sockets.TcpClient
    $iar = $client.BeginConnect($hostName, $port, $null, $null)
    $ok = $iar.AsyncWaitHandle.WaitOne($timeoutMs, $false)
    if (-not $ok) { $client.Close(); return $false }
    $client.EndConnect($iar)
    $client.Close()
    return $true
  } catch {
    return $false
  }
}

function Wait-PostgresReady([int]$maxSeconds = 180) {
  $deadline = (Get-Date).AddSeconds($maxSeconds)
  $pgIsReady = 'C:\Program Files\PostgreSQL\18\bin\pg_isready.exe'
  while ((Get-Date) -lt $deadline) {
    if (Test-Path $pgIsReady) {
      & $pgIsReady -h 127.0.0.1 -p 5432 2>$null | Out-Null
      if ($LASTEXITCODE -eq 0) { return $true }
    } elseif (Test-TcpPort '127.0.0.1' 5432) {
      return $true
    }
    Start-Sleep -Seconds 2
  }
  return $false
}

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://0.0.0.0:5080'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$dotnetRootCandidates = @(
  'C:\ShiftClub\dotnet',
  'C:\Program Files\dotnet',
  'D:\Dev\dotnet'
)
$dotnetExe = $null
foreach ($root in $dotnetRootCandidates) {
  $candidate = Join-Path $root 'dotnet.exe'
  if (Test-Path $candidate) {
    $env:DOTNET_ROOT = $root
    $env:PATH = "$root;$env:PATH"
    $dotnetExe = $candidate
    break
  }
}

$secrets = Join-Path $PSScriptRoot 'secrets.env'
if (Test-Path $secrets) {
  Get-Content $secrets | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith('#')) { return }
    $i = $line.IndexOf('=')
    if ($i -lt 1) { return }
    $name = $line.Substring(0, $i).Trim()
    $value = $line.Substring($i + 1).Trim()
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
  }
  Write-BootLog 'Loaded secrets.env'
} else {
  Write-BootLog 'WARNING: secrets.env missing'
}

if (-not $env:ConnectionStrings__Default) {
  throw 'ConnectionStrings__Default is not set. Create secrets.env from secrets.env.example'
}
if (-not $env:Jwt__SigningKey) {
  throw 'Jwt__SigningKey is not set. Create secrets.env from secrets.env.example'
}

Write-BootLog 'Waiting for PostgreSQL on 127.0.0.1:5432...'
if (-not (Wait-PostgresReady 180)) {
  throw 'PostgreSQL not ready after 180s'
}
Write-BootLog 'PostgreSQL is ready'

$dll = Join-Path $PSScriptRoot 'ShiftClub.Server.dll'
$exe = Join-Path $PSScriptRoot 'ShiftClub.Server.exe'

$restartDelaySec = 5
$attempt = 0
while ($true) {
  $attempt++
  try {
    if ($dotnetExe -and (Test-Path $dll)) {
      Write-BootLog "Starting API attempt=$attempt via $dotnetExe"
      & $dotnetExe $dll --urls http://0.0.0.0:5080
      $code = $LASTEXITCODE
    } elseif (Test-Path $exe) {
      Write-BootLog "Starting API attempt=$attempt via $exe"
      & $exe --urls http://0.0.0.0:5080
      $code = $LASTEXITCODE
    } else {
      throw "Neither dotnet+dll nor ShiftClub.Server.exe found under $PSScriptRoot"
    }
    Write-BootLog "API exited code=$code - restart in ${restartDelaySec}s"
  } catch {
    Write-BootLog "API crash: $($_.Exception.Message) - restart in ${restartDelaySec}s"
  }
  Start-Sleep -Seconds $restartDelaySec
  if ($restartDelaySec -lt 60) { $restartDelaySec = [Math]::Min(60, $restartDelaySec * 2) }

  if (-not (Wait-PostgresReady 60)) {
    Write-BootLog 'PostgreSQL not ready during restart wait - keep waiting'
    [void](Wait-PostgresReady 300)
  }
}
