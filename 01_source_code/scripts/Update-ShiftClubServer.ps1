<#
.SYNOPSIS
  Ставит новую версию сервера клуба с откатом, если она не поднялась.

.DESCRIPTION
  Запускается на сервере клуба — обычно его запускает сам сервер по кнопке
  «Обновить» в панели (Состояние системы), но его можно запустить и руками.

  Порядок такой:
    1. остановить сторожа и сам сервер (иначе сторож поднимет его посреди подмены);
    2. отложить текущую папку сервера целиком в rollback\<версия-время>;
    3. распаковать пакет, вернуть на место настройки и данные клуба;
    4. поднять сервер и дождаться, пока /health ответит;
    5. если не ответил — вернуть отложенную папку и поднять старую версию.

  Копию базы делает сервер перед запуском этого скрипта. Если скрипт
  запускают руками, сделайте копию сами: панель → Копии базы → «Сделать копию».

.EXAMPLE
  .\Update-ShiftClubServer.ps1 -PackagePath C:\ShiftClub\Server\data\server-updates\ShiftClub.Server-0.7.0.zip -Version 0.7.0
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$PackagePath,

  [Parameter(Mandatory = $true)]
  [string]$Version,

  [string]$ServerDir = "C:\ShiftClub\Server",
  [string]$ReportPath = "",
  [string]$HealthUrl = "http://127.0.0.1:5080/health",
  [string]$TaskName = "ShiftClubApi",
  [string]$WatchdogTaskName = "ShiftClubApiWatchdog",

  # Сколько ждать, пока новая версия ответит на /health. Первый запуск дольше:
  # накатываются миграции.
  [int]$HealthTimeoutSeconds = 240,

  # Сколько откатов держать на диске.
  [int]$KeepRollbacks = 3
)

$ErrorActionPreference = "Stop"

# Что сохраняем от клуба: настройки, секреты, данные, логи и публичную статику,
# которую клуб правил у себя.
$PreserveItems = @(
  'appsettings.Production.json',
  'appsettings.Development.json',
  'secrets.env',
  'data',
  'logs'
)
$PreserveWwwFolders = @('site', 'price', 'promo', 'tg-webapp', 'product', 'work', 'media', 'uploads')

# Откаты, распаковка и журнал — рядом с папкой сервера, а не внутри неё и не в
# TEMP: подмена делается переименованием (работает только внутри одного диска),
# а в саму папку сервера во время подмены писать нельзя.
$serverParent = Split-Path $ServerDir -Parent
if (-not $serverParent) { throw "Не удалось определить папку выше $ServerDir" }

$logFile = Join-Path $serverParent 'shiftclub-update.log'
$startedAt = (Get-Date).ToUniversalTime()

function Write-Log([string]$message) {
  $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $message
  Write-Host $line
  try {
    Add-Content -Path $logFile -Value $line -Encoding UTF8 -ErrorAction Stop
  } catch {
    Write-Host "(журнал недоступен: $($_.Exception.Message))"
  }
}

function Write-Report([string]$state, [string]$message, [switch]$Finished) {
  if (-not $ReportPath) { return }
  try {
    $dir = Split-Path $ReportPath -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $finishedAt = $null
    if ($Finished) { $finishedAt = (Get-Date).ToUniversalTime().ToString("o") }

    [ordered]@{
      version    = $Version
      state      = $state
      message    = $message
      startedAt  = $startedAt.ToString("o")
      finishedAt = $finishedAt
    } | ConvertTo-Json | Set-Content -Path $ReportPath -Encoding UTF8
  } catch {
    Write-Log "Не удалось записать отчёт: $($_.Exception.Message)"
  }
}

function Stop-Api {
  foreach ($task in @($WatchdogTaskName, $TaskName)) {
    if (-not $task) { continue }
    try {
      $t = Get-ScheduledTask -TaskName $task -ErrorAction Stop
      Disable-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue | Out-Null
      if ($t.State -eq 'Running') { & schtasks.exe /End /TN $task | Out-Null }
      Write-Log "Задача $task остановлена и выключена"
    } catch {
      Write-Log "Задачи $task нет — пропускаем"
    }
  }

  # Сервер мог быть запущен руками, не задачей: добираем по пути к файлам.
  Get-Process -Name 'ShiftClub.Server' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($ServerDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
      Write-Log "Закрываем процесс сервера PID=$($_.Id)"
      try { $_.Kill() } catch { Write-Log "Не удалось закрыть PID=$($_.Id): $($_.Exception.Message)" }
    }

  # dotnet.exe ShiftClub.Server.dll — так сервер запускает Start-ShiftClubApi.ps1.
  # Командную строку процесса видно только через CIM, и его может не быть.
  if (Get-Command Get-CimInstance -ErrorAction SilentlyContinue) {
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
      Where-Object { $_.CommandLine -and $_.CommandLine -like '*ShiftClub.Server.dll*' } |
      ForEach-Object {
        Write-Log "Закрываем dotnet PID=$($_.ProcessId)"
        try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { Write-Log "Не удалось: $($_.Exception.Message)" }
      }
  }

  Start-Sleep -Seconds 3
}

# Не бросает исключений: любая неудача запуска должна привести к откату по
# проверке /health, а не к обрыву скрипта с сервером в разобранном виде.
function Start-Api {
  try {
    Enable-ScheduledTask -TaskName $TaskName -ErrorAction Stop | Out-Null
    Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    Write-Log "Задача $TaskName запущена"
    return
  } catch {
    Write-Log "Не удалось запустить задачу $TaskName : $($_.Exception.Message)"
  }

  $starter = Join-Path $ServerDir 'Start-ShiftClubApi.ps1'
  if (-not (Test-Path $starter)) {
    Write-Log "Нечем запустить сервер: нет ни задачи $TaskName, ни $starter"
    return
  }

  try {
    Write-Log "Запускаем $starter напрямую"
    Start-Process -FilePath 'powershell.exe' `
      -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', $starter) `
      -WorkingDirectory $ServerDir | Out-Null
  } catch {
    Write-Log "Запуск $starter не удался: $($_.Exception.Message)"
  }
}

function Enable-Watchdog {
  if (-not $WatchdogTaskName) { return }
  try {
    Enable-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction Stop | Out-Null
    Write-Log "Сторож $WatchdogTaskName включён"
  } catch {
    Write-Log "Сторожа $WatchdogTaskName нет — пропускаем"
  }
}

function Wait-Healthy([int]$timeoutSeconds) {
  $deadline = (Get-Date).AddSeconds($timeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    try {
      $r = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 5
      if ($r.StatusCode -eq 200) { return $true }
    } catch {
      # Сервер ещё поднимается — это ожидаемо.
    }
    Start-Sleep -Seconds 5
  }
  return $false
}

function Copy-Preserved([string]$from, [string]$to) {
  foreach ($item in $PreserveItems) {
    $src = Join-Path $from $item
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $to $item
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    Copy-Item $src $dst -Recurse -Force
    Write-Log "Сохранено: $item"
  }

  foreach ($folder in $PreserveWwwFolders) {
    $src = Join-Path (Join-Path $from 'wwwroot') $folder
    if (-not (Test-Path $src)) { continue }
    $dstRoot = Join-Path $to 'wwwroot'
    if (-not (Test-Path $dstRoot)) { New-Item -ItemType Directory -Force -Path $dstRoot | Out-Null }
    $dst = Join-Path $dstRoot $folder
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    Copy-Item $src $dst -Recurse -Force
    Write-Log "Сохранено: wwwroot\$folder"
  }
}

# --- Проверки до того, как что-то ломать ---

if (-not (Test-Path $PackagePath)) { throw "Нет пакета: $PackagePath" }
if (-not (Test-Path $ServerDir)) { throw "Нет папки сервера: $ServerDir" }

$rollbackRoot = Join-Path $serverParent 'server-rollback'
$rollbackDir = Join-Path $rollbackRoot ("{0}-{1:yyyyMMdd-HHmmss}" -f $Version, (Get-Date))
$extractDir = Join-Path $serverParent ("server-new-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))

Write-Log "=== Обновление до $Version из $PackagePath ==="
Write-Report 'swapping' 'Распаковка пакета.'

try {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
  [System.IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $extractDir)

  if (-not (Test-Path (Join-Path $extractDir 'ShiftClub.Server.dll'))) {
    throw "В пакете нет ShiftClub.Server.dll — это не пакет сервера"
  }
  Write-Log "Пакет распакован в $extractDir"
} catch {
  $msg = "Пакет не распаковался: $($_.Exception.Message)"
  Write-Log $msg
  Write-Report 'failed' $msg -Finished
  if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue }
  throw
}

# Настройки и данные переносим в распакованное ДО остановки сервера: если здесь
# что-то не так, клуб продолжает работать на старой версии.
try {
  Copy-Preserved -from $ServerDir -to $extractDir
} catch {
  $msg = "Не удалось перенести настройки клуба: $($_.Exception.Message)"
  Write-Log $msg
  Write-Report 'failed' $msg -Finished
  Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
  throw
}

# --- С этого места сервер останавливается ---

Stop-Api
Write-Report 'swapping' 'Подмена файлов сервера.'

New-Item -ItemType Directory -Force -Path $rollbackRoot | Out-Null
$movedToRollback = $false
try {
  Move-Item $ServerDir $rollbackDir
  $movedToRollback = $true

  # Между переносами с папкой сервера нельзя делать ничего: стоит ей появиться
  # (хватит и записи в журнал внутри неё), и второй перенос положит новую версию
  # внутрь папки вместо подмены.
  Move-Item $extractDir $ServerDir

  Write-Log "Старая версия отложена в $rollbackDir"
  Write-Log "Новая версия на месте: $ServerDir"
} catch {
  $msg = "Подмена файлов не удалась: $($_.Exception.Message)"
  Write-Log $msg
  if ($movedToRollback -and -not (Test-Path $ServerDir)) {
    Move-Item $rollbackDir $ServerDir
    Write-Log 'Старая версия возвращена на место'
  }
  Start-Api
  Enable-Watchdog
  Write-Report 'rolled_back' $msg -Finished
  throw
}

Write-Report 'swapping' 'Запуск новой версии.'
Start-Api

if (Wait-Healthy $HealthTimeoutSeconds) {
  Enable-Watchdog
  Write-Log "Версия $Version поднялась, /health отвечает"
  Write-Report 'ok' "Версия $Version установлена." -Finished

  # Лишние откаты занимают место, а полезен только последний.
  Get-ChildItem $rollbackRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object CreationTime -Descending |
    Select-Object -Skip $KeepRollbacks |
    ForEach-Object {
      Write-Log "Удаляем старый откат $($_.Name)"
      Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

  Write-Log '=== Готово ==='
  exit 0
}

Write-Log "Новая версия не ответила за ${HealthTimeoutSeconds}с — откатываемся"
Stop-Api

$failedDir = Join-Path $rollbackRoot ("failed-{0}-{1:yyyyMMdd-HHmmss}" -f $Version, (Get-Date))
try {
  Move-Item $ServerDir $failedDir
  Move-Item $rollbackDir $ServerDir
  Write-Log "Откат выполнен, неудачная версия осталась в $failedDir"
} catch {
  $msg = "Откат не удался: $($_.Exception.Message). Папка сервера может быть в $rollbackDir — верните её вручную."
  Write-Log $msg
  Write-Report 'failed' $msg -Finished
  throw
}

Start-Api
Enable-Watchdog

if (Wait-Healthy $HealthTimeoutSeconds) {
  $msg = "Версия $Version не поднялась, вернули прежнюю. Файлы неудачной версии: $failedDir"
  Write-Log $msg
  Write-Report 'rolled_back' $msg -Finished
  exit 1
}

$msg = "Версия $Version не поднялась, и прежняя тоже не отвечает. Нужны руки: журнал $logFile"
Write-Log $msg
Write-Report 'failed' $msg -Finished
exit 2
