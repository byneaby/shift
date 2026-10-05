# Подготовка сервера нового клуба: роль и база в PostgreSQL, secrets.env со
# случайными паролями, проверка pg_dump. Запускать один раз, до первого старта API.
#
# Пример:
#   .\New-ShiftClub.ps1 -ClubName 'Nexus Arena' -Address 'г. Астана, ул. Кенесары 24' `
#       -LicenseKey 'SHIFT1....' -ServerPath 'C:\ShiftClub\Server'
#
# Скрипт ничего не перезаписывает: если secrets.env уже есть, он останавливается.
# Пароли печатаются один раз — сохраните их в менеджер паролей сразу.

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$ClubName,

  [string]$Address = '',
  [string]$TimeZone = 'Asia/Almaty',
  [string]$Currency = 'KZT',

  [string]$ServerPath = 'C:\ShiftClub\Server',
  [string]$DbName = 'shiftclub',
  [string]$DbUser = 'shiftclub',
  [string]$DbHost = '127.0.0.1',
  [int]$DbPort = 5432,

  # Пароль суперпользователя postgres. Нужен только на время создания базы.
  [string]$PostgresPassword,

  # Путь к bin PostgreSQL. Если не задан, ищем сами.
  [string]$PgBin,

  [string]$LicenseKey = '',
  [string]$LicensePublicKey = '',

  [switch]$SkipDatabase,
  [switch]$Force
)

$ErrorActionPreference = 'Stop'

function New-RandomSecret([int]$bytes) {
  $buffer = New-Object byte[] $bytes
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
  return [Convert]::ToBase64String($buffer)
}

function New-RandomPassword([int]$length = 16) {
  # Без похожих друг на друга символов: пароль будут диктовать по телефону.
  $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'
  $chars = New-Object char[] $length
  for ($i = 0; $i -lt $length; $i++) {
    $index = [System.Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Length)
    $chars[$i] = $alphabet[$index]
  }
  return -join $chars
}

function New-RandomPin([int]$length = 6) {
  $digits = New-Object char[] $length
  for ($i = 0; $i -lt $length; $i++) {
    $digits[$i] = [char]([int][char]'0' + [System.Security.Cryptography.RandomNumberGenerator]::GetInt32(10))
  }
  return -join $digits
}

function Resolve-PgBin([string]$explicit) {
  if ($explicit) {
    if (-not (Test-Path (Join-Path $explicit 'psql.exe'))) {
      throw "В папке '$explicit' нет psql.exe"
    }
    return $explicit
  }

  $psqlInPath = Get-Command psql.exe -ErrorAction SilentlyContinue
  if ($psqlInPath) { return (Split-Path $psqlInPath.Source -Parent) }

  $roots = Get-ChildItem 'C:\Program Files\PostgreSQL' -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending
  foreach ($root in $roots) {
    $bin = Join-Path $root.FullName 'bin'
    if (Test-Path (Join-Path $bin 'psql.exe')) { return $bin }
  }

  throw 'Не нашли psql.exe. Установите PostgreSQL или укажите -PgBin.'
}

function Invoke-Psql([string]$bin, [string]$sql, [string]$database = 'postgres') {
  $psql = Join-Path $bin 'psql.exe'
  $output = & $psql -h $DbHost -p $DbPort -U postgres -d $database -v ON_ERROR_STOP=1 -t -A -c $sql 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "psql не выполнил запрос: $output"
  }
  return ($output | Out-String).Trim()
}

function Quote-Literal([string]$value) {
  return "'" + $value.Replace("'", "''") + "'"
}

# --- Проверки до изменений ---

$secretsPath = Join-Path $ServerPath 'secrets.env'
if ((Test-Path $secretsPath) -and -not $Force) {
  throw "Файл '$secretsPath' уже существует. Это рабочий клуб — проверьте, что запускаете скрипт там, где нужно. Перезаписать: -Force"
}

if (-not (Test-Path $ServerPath)) {
  New-Item -ItemType Directory -Path $ServerPath -Force | Out-Null
  Write-Host "Создана папка $ServerPath"
}

try {
  [void][System.TimeZoneInfo]::FindSystemTimeZoneById($TimeZone)
} catch {
  throw "Часовой пояс '$TimeZone' система не знает. Список: Get-TimeZone -ListAvailable"
}

$pgBin = Resolve-PgBin $PgBin
Write-Host "PostgreSQL: $pgBin"

$pgDump = Join-Path $pgBin 'pg_dump.exe'
if (-not (Test-Path $pgDump)) {
  Write-Warning "Не нашли $pgDump — копии базы работать не будут. Укажите Backup__PgDumpPath в secrets.env."
  $pgDump = $null
}

# --- Пароли ---

$dbPassword = New-RandomPassword 20
$ownerPassword = New-RandomPassword 14
$shellAdminPassword = New-RandomPassword 12
$hiringPin = New-RandomPin 6
$jwtKey = New-RandomSecret 48

# --- База ---

if (-not $SkipDatabase) {
  if (-not $PostgresPassword) {
    $secure = Read-Host "Пароль пользователя postgres" -AsSecureString
    $PostgresPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
      [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
  }

  # Пароль передаём только через переменную окружения процесса: в командной
  # строке его видно всем, кто смотрит список процессов.
  $env:PGPASSWORD = $PostgresPassword
  try {
    $roleExists = Invoke-Psql $pgBin "SELECT 1 FROM pg_roles WHERE rolname = $(Quote-Literal $DbUser)"
    if ($roleExists -eq '1') {
      Write-Host "Роль $DbUser уже есть — меняем ей пароль"
      Invoke-Psql $pgBin "ALTER ROLE `"$DbUser`" WITH LOGIN PASSWORD $(Quote-Literal $dbPassword)" | Out-Null
    } else {
      Invoke-Psql $pgBin "CREATE ROLE `"$DbUser`" WITH LOGIN PASSWORD $(Quote-Literal $dbPassword)" | Out-Null
      Write-Host "Создана роль $DbUser"
    }

    $dbExists = Invoke-Psql $pgBin "SELECT 1 FROM pg_database WHERE datname = $(Quote-Literal $DbName)"
    if ($dbExists -eq '1') {
      Write-Host "База $DbName уже есть — оставляем как есть"
    } else {
      Invoke-Psql $pgBin "CREATE DATABASE `"$DbName`" OWNER `"$DbUser`" ENCODING 'UTF8'" | Out-Null
      Write-Host "Создана база $DbName"
    }

    Invoke-Psql $pgBin "GRANT ALL ON SCHEMA public TO `"$DbUser`"" $DbName | Out-Null
  } finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
  }
} else {
  Write-Host 'Создание базы пропущено (-SkipDatabase). Пароль роли в secrets.env придётся поправить вручную.'
}

# --- secrets.env ---

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Создан New-ShiftClub.ps1 ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
$lines.Add('# В git не попадает. Пароли внутри — единственная копия, кроме вашего менеджера паролей.')
$lines.Add('')
$lines.Add('# --- База данных ---')
$lines.Add("ConnectionStrings__Default=Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$dbPassword")
$lines.Add('')
$lines.Add('# --- Вход в панель ---')
$lines.Add("Jwt__SigningKey=$jwtKey")
$lines.Add('Jwt__ExpirationMinutes=720')
$lines.Add('Seed__OwnerLogin=owner')
$lines.Add("Seed__OwnerPassword=$ownerPassword")
$lines.Add("Seed__ShellAdminPassword=$shellAdminPassword")
$lines.Add('EnableSwagger=false')
$lines.Add('')
$lines.Add('# --- Клуб ---')
$lines.Add("Seed__ClubName=$ClubName")
$lines.Add('Seed__BranchCode=MAIN')
$lines.Add("Seed__TimeZone=$TimeZone")
$lines.Add("Seed__Currency=$Currency")
$lines.Add("Seed__Address=$Address")
$lines.Add('')
$lines.Add('# --- Лицензия ---')
$lines.Add('License__Enforce=true')
if ($LicensePublicKey) {
  $lines.Add("License__PublicKey=$LicensePublicKey")
} else {
  $lines.Add('License__PublicKey=CHANGE_ME_VENDOR_PUBLIC_KEY')
}
if ($LicenseKey) {
  $lines.Add("License__Key=$LicenseKey")
} else {
  $lines.Add('# Ключ можно вставить в панели: Система -> Лицензия')
  $lines.Add('# License__Key=SHIFT1....')
}
$lines.Add('')
$lines.Add('# --- Копии базы ---')
$lines.Add('Backup__Enabled=true')
$lines.Add('Backup__DailyHourLocal=6')
$lines.Add('Backup__KeepDays=14')
if ($pgDump) {
  $lines.Add("Backup__PgDumpPath=$pgDump")
} else {
  $lines.Add('# Backup__PgDumpPath=C:\Program Files\PostgreSQL\16\bin\pg_dump.exe')
}
$lines.Add('')
$lines.Add('# --- Безопасность ---')
$lines.Add('# Локальная сеть разрешена всегда. Сюда добавлять только внешние домены панели.')
$lines.Add('# Cors__AllowedOrigins__0=https://panel.example.kz')
$lines.Add('Security__LoginAttemptsPerMinute=10')
$lines.Add('')
$lines.Add('# --- Панель найма ---')
$lines.Add("Hiring__ManagerPin=$hiringPin")
$lines.Add('Hiring__SessionHours=12')
$lines.Add('')
$lines.Add('# --- Telegram (свой бот от @BotFather) ---')
$lines.Add('# Telegram__BotToken=')

Set-Content -Path $secretsPath -Value $lines -Encoding UTF8
Write-Host "Записан $secretsPath"

# --- Что сохранить ---

Write-Host ''
Write-Host '=== Сохраните это сейчас, второй раз не покажем ===' -ForegroundColor Yellow
Write-Host ("Клуб:                  {0}" -f $ClubName)
Write-Host ("Вход в панель:         owner / {0}" -f $ownerPassword)
Write-Host ("Админ-режим Shell:     {0}" -f $shellAdminPassword)
Write-Host ("ПИН панели найма:      {0}" -f $hiringPin)
Write-Host ("Пароль роли БД:        {0}" -f $dbPassword)
Write-Host '==================================================' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Дальше:'
Write-Host ("  1. Положить файлы сервера в {0}" -f $ServerPath)
if (-not $LicensePublicKey) {
  Write-Host '  2. Вписать License__PublicKey в secrets.env (выдаёт поставщик)'
}
Write-Host '  3. Запустить .\Start-ShiftClubApi.ps1 — база создастся миграциями'
Write-Host '  4. Войти в панель и пройти «Система -> Настройка клуба»'
