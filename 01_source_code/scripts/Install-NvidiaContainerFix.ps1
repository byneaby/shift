<#
.SYNOPSIS
  Надёжный фикс NVIDIA Control Panel на CCBoot БЕЗ SuperClient.

.DESCRIPTION
  Почему в SuperClient работает, а в обычном режиме — нет:
    InfGPU / PnP подсовывает драйвер ПОСЛЕ старта Windows. Служба
    NVDisplay.ContainerLocalSystem (Automatic) успевает стартовать РАНЬШЕ GPU →
    процесс "Running", но зомби. Ручной restart оживляет. В SuperClient
    драйвер уже в образе — служба стартует нормально.

  Что делает этот скрипт (один раз в образе / SuperClient → Save):
    1) Ставит NVDisplay.ContainerLocalSystem в тип запуска "Вручную" (demand),
       чтобы не стартовала зомби при раннем буте.
    2) Ставит SYSTEM-задачу, которая ЖДЁТ появления NVIDIA GPU, потом
       убивает старый процесс и стартует службу (с ретраями).

.EXAMPLE
  # Двойной клик (обходит ExecutionPolicy):
  .\Install-NvidiaContainerFix.cmd

  # Или вручную:
  powershell -NoProfile -ExecutionPolicy Bypass -File ".\Install-NvidiaContainerFix.ps1"
#>
param(
  [switch]$Uninstall,
  [string]$TaskName = "ShiftClub-NvidiaGpuReady"
)

$ErrorActionPreference = "Stop"

$scriptDir = "C:\ShiftClub\Scripts"
$scriptPath = Join-Path $scriptDir "Start-NvidiaContainerWhenGpuReady.ps1"
$logPath = "C:\ProgramData\ShiftClub\nvidia-gpu-ready.log"

$watchScript = @'
# Wait for NVIDIA GPU (CCBoot InfGPU), then start/bounce Display Container.
# Runs as SYSTEM. Safe to re-run.
$ErrorActionPreference = "SilentlyContinue"
$log = "C:\ProgramData\ShiftClub\nvidia-gpu-ready.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
function Log($m) {
  $line = "$(Get-Date -Format o) $m"
  Add-Content -Path $log -Value $line -ErrorAction SilentlyContinue
}

function Test-NvidiaGpuReady {
  try {
    $gpus = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue
    foreach ($g in $gpus) {
      $n = [string]$g.Name
      if ($n -match "NVIDIA|GeForce|RTX|GTX|Quadro") {
        if ($g.Status -eq "OK" -or $g.Availability -eq 3) { return $true }
        # даже без Status=OK, если имя NVIDIA — PnP уже отдал устройство
        return $true
      }
    }
  } catch {}

  # Fallback: драйвер nvlddmkm загружен
  try {
    $drv = Get-CimInstance Win32_SystemDriver -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -eq "nvlddmkm" -and $_.State -eq "Running" }
    if ($drv) { return $true }
  } catch {}

  # Fallback: nvidia-smi отвечает
  try {
    $smi = @(
      "$env:ProgramFiles\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
      "$env:SystemRoot\System32\nvidia-smi.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($smi) {
      $p = Start-Process -FilePath $smi -ArgumentList "-L" -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\nvsmi.out" -RedirectStandardError "$env:TEMP\nvsmi.err"
      if ($p.ExitCode -eq 0) { return $true }
    }
  } catch {}

  return $false
}

function Bounce-Container {
  foreach ($name in @("NVDisplay.Container", "NVDisplayContainer")) {
    & taskkill.exe /F /IM "$name.exe" /T 2>$null | Out-Null
  }
  Start-Sleep -Milliseconds 800

  $svcNames = @(
    "NVDisplay.ContainerLocalSystem",
    "NVDisplayContainerLocalSystem",
    "NvContainerLocalSystem"
  )

  foreach ($svc in $svcNames) {
    $q = & sc.exe query "$svc" 2>$null
    if ($LASTEXITCODE -ne 0) { continue }

    Log "bounce service $svc"
    & sc.exe stop "$svc" | Out-Null
    Start-Sleep -Seconds 2
    & taskkill.exe /F /IM "NVDisplay.Container.exe" /T 2>$null | Out-Null
    & sc.exe start "$svc" | Out-Null
    Start-Sleep -Seconds 3

    $alive = Get-Process -Name "NVDisplay.Container" -ErrorAction SilentlyContinue
    Log "process alive=$([bool]$alive)"
    return [bool]$alive
  }

  # Direct exe from DriverStore (hash folder changes with driver)
  $repo = "$env:SystemRoot\System32\DriverStore\FileRepository"
  if (Test-Path $repo) {
    $exe = Get-ChildItem $repo -Filter "NVDisplay.Container.exe" -Recurse -ErrorAction SilentlyContinue |
      Select-Object -First 1
    if ($exe) {
      Log "start exe $($exe.FullName)"
      Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName -WindowStyle Hidden
      Start-Sleep -Seconds 2
      return [bool](Get-Process -Name "NVDisplay.Container" -ErrorAction SilentlyContinue)
    }
  }
  return $false
}

Log "=== gpu-ready watcher start ==="
$deadline = (Get-Date).AddMinutes(12)
$ready = $false
while ((Get-Date) -lt $deadline) {
  if (Test-NvidiaGpuReady) {
    Log "NVIDIA GPU detected"
    $ready = $true
    break
  }
  Start-Sleep -Seconds 3
}

if (-not $ready) {
  Log "WARNING: NVIDIA GPU not detected within timeout — bounce anyway"
}

# 2–3 попытки: иногда первый start после PnP ещё сырой
for ($i = 1; $i -le 3; $i++) {
  Log "bounce attempt $i"
  if (Bounce-Container) {
    Log "SUCCESS on attempt $i"
    break
  }
  Start-Sleep -Seconds 5
}

# Ещё один delayed bounce (как «ручной» через минуту)
Start-Sleep -Seconds 45
Log "final delayed bounce"
[void](Bounce-Container)
Log "=== gpu-ready watcher done ==="
'@

if ($Uninstall) {
  foreach ($t in @($TaskName, "$TaskName-Boot", "$TaskName-Logon")) {
    schtasks.exe /Delete /TN $t /F 2>$null | Out-Null
  }
  if (Test-Path $scriptPath) { Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue }
  # Вернуть Automatic — опционально; лучше оставить Manual если фикс снят осознанно
  Write-Host "Removed tasks. Service start type left unchanged (check sc qc NVDisplay.ContainerLocalSystem)."
  exit 0
}

New-Item -ItemType Directory -Force -Path $scriptDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null
Set-Content -Path $scriptPath -Value $watchScript -Encoding UTF8

# 1) Не стартовать контейнер до GPU — иначе зомби на каждом бездисковом буте
$svc = "NVDisplay.ContainerLocalSystem"
$qc = & sc.exe query $svc 2>$null
if ($LASTEXITCODE -eq 0) {
  & sc.exe config $svc start= demand | Out-Host
  Write-Host "Service $svc -> start=demand (manual)"
} else {
  Write-Warning "Service $svc not found yet (OK if driver not in this SuperClient session)."
}

# 2) Задачи SYSTEM: при старте системы и при входе
$tr = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`""
# ONSTART сразу (скрипт сам ждёт GPU до 12 мин)
schtasks.exe /Create /TN "$TaskName-Boot" /SC ONSTART /RU SYSTEM /RL HIGHEST /TR $tr /F | Out-Host
# ONLOGON — страховка, если ONSTART прошёл до PnP-хука CCBoot
schtasks.exe /Create /TN "$TaskName-Logon" /SC ONLOGON /DELAY 0000:20 /RU SYSTEM /RL HIGHEST /TR $tr /F | Out-Host

Write-Host ""
Write-Host "Installed OK:"
Write-Host "  Script: $scriptPath"
Write-Host "  Tasks:  $TaskName-Boot (ONSTART SYSTEM), $TaskName-Logon (ONLOGON+20s)"
Write-Host "  Log:    $logPath"
Write-Host ""
Write-Host "NEXT: Save CCBoot image / disable SuperClient, reboot a client, open NVIDIA CPL without manual restart."
Write-Host "Test now:  schtasks /Run /TN $TaskName-Boot"
