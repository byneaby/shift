<#
.SYNOPSIS
  Собирает один EXE установщика сервера SHIFT Club с мастером настройки.
  Внутри уже есть сервер с .NET, PostgreSQL 16 и Visual C++.

.EXAMPLE
  .\scripts\Build-ServerInstaller.ps1

Нужен .NET SDK 8 x64 (не только runtime) и доступ в интернет при первой сборке.
Путь к dotnet берётся из PATH, отдельную папку D:\Dev\dotnet указывать не нужно.
Архив PostgreSQL и vc_redist в git не коммитятся.
#>
param(
  [string]$Version = "1.0.0",
  [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

# Keep these in sync with PostgresBundle in ShiftClub.Server.Setup.
$PostgresUrl = "https://get.enterprisedb.com/postgresql/postgresql-16.15-5-windows-x64-binaries.zip"
$PostgresSha256 = "43bb45f173a6f08cf1d29a97a6d8deb119e8e8093a24c00d2d1001a0ccaa8281"
$VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"

function Save-Url([string]$Url, [string]$Dest) {
  $dir = Split-Path $Dest -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
    $effective = & curl.exe -fL --retry 3 --retry-delay 2 -sS -o $Dest -w "%{url_effective}" $Url
    if ($LASTEXITCODE -ne 0) { throw "Не удалось скачать $Url" }
    return "$effective".Trim()
  }
  Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing
  return $Url
}

function Get-Sha256([string]$Path) {
  return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Test-ZipEntry([string]$ZipPath, [string]$Entry) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
  try {
    foreach ($item in $zip.Entries) {
      if ($item.FullName.Replace('\', '/') -eq $Entry) { return $true }
    }
    return $false
  } finally {
    $zip.Dispose()
  }
}

function New-PostgresRuntimeZip([string]$SourceZip, [string]$DestZip) {
  if (Test-Path $DestZip) { Remove-Item $DestZip -Force }
  $src = [System.IO.Compression.ZipFile]::OpenRead($SourceZip)
  $dst = [System.IO.Compression.ZipFile]::Open($DestZip, [System.IO.Compression.ZipArchiveMode]::Create)
  try {
    $kept = 0
    foreach ($entry in $src.Entries) {
      $name = $entry.FullName.Replace('\', '/')
      if ([string]::IsNullOrEmpty($name) -or $name.EndsWith('/')) { continue }
      $relative = $name
      if ($relative.StartsWith("pgsql/")) { $relative = $relative.Substring(6) }
      $keep = $relative.StartsWith("bin/") -or $relative.StartsWith("lib/") -or $relative.StartsWith("share/") -or $relative.StartsWith("include/")
      if (-not $keep) { continue }
      if ($relative.Contains("..")) { continue }
      $created = $dst.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
      $inStream = $entry.Open()
      $outStream = $created.Open()
      try { $inStream.CopyTo($outStream) } finally { $outStream.Dispose(); $inStream.Dispose() }
      $kept++
      if (($kept % 200) -eq 0) { Write-Host "  файлов PostgreSQL: $kept" }
    }
    if ($kept -lt 10) { throw "В архиве PostgreSQL нет файлов сервера." }
    Write-Host "  в установщик вошло файлов PostgreSQL: $kept"
  } finally {
    $dst.Dispose()
    $src.Dispose()
  }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "Не найден .NET SDK 8. Установите SDK x64: https://dotnet.microsoft.com/download/dotnet/8.0"
}

if (-not $RepoRoot) {
  $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$serverProj = Join-Path $RepoRoot "src\ShiftClub.Server\ShiftClub.Server.csproj"
$installerProj = Join-Path $RepoRoot "src\ShiftClub.Server.Installer\ShiftClub.Server.Installer.csproj"
$payloadDir = Join-Path $RepoRoot "src\ShiftClub.Server.Installer\payload"
$stage = Join-Path $RepoRoot "dist\server-installer-stage\$Version"
$serverOut = Join-Path $stage "server"
$zipPath = Join-Path $payloadDir "server.zip"
$outDir = Join-Path $RepoRoot "dist\installer"
$finalExe = Join-Path $outDir "ShiftClub.Server.Setup-$Version.exe"

Write-Host "== SHIFT Club Server Installer $Version =="
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $serverOut, $payloadDir, $outDir | Out-Null

Write-Host "== Publish server (win-x64, self-contained) =="
dotnet publish $serverProj -c Release -r win-x64 --self-contained true -o $serverOut -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Публикация сервера не удалась" }

$web = Join-Path $RepoRoot "src\ShiftClub.Web"
if (Get-Command npm -ErrorAction SilentlyContinue) {
  Write-Host "== Build staff panel =="
  Push-Location $web
  npm run build
  if ($LASTEXITCODE -ne 0) { throw "Сборка панели не удалась" }
  Pop-Location
  $www = Join-Path $serverOut "wwwroot"
  New-Item -ItemType Directory -Force -Path (Join-Path $www "assets") | Out-Null
  Copy-Item (Join-Path $web "dist\index.html") (Join-Path $www "index.html") -Force
  Copy-Item (Join-Path $web "dist\assets\*") (Join-Path $www "assets") -Force
} else {
  Write-Warning "npm не найден, в установщик попадёт панель из wwwroot проекта."
}

Copy-Item (Join-Path $RepoRoot "scripts\Start-ShiftClubApi.ps1") $serverOut -Force
Copy-Item (Join-Path $RepoRoot "scripts\Watch-ShiftClubApi.ps1") $serverOut -Force
foreach ($name in @("secrets.env", "data", "logs")) {
  $path = Join-Path $serverOut $name
  if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host "== Pack payload =="
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($serverOut, $zipPath)

$cacheDir = Join-Path $RepoRoot "dist\cache"
$officialZip = Join-Path $cacheDir "postgresql-16.15-5-windows-x64-binaries.zip"
$postgresZip = Join-Path $payloadDir "postgres.zip"
$vcRedist = Join-Path $payloadDir "vc_redist.x64.exe"
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null

$havePostgres = (Test-Path $officialZip) -and ((Get-Sha256 $officialZip) -eq $PostgresSha256)
if (-not $havePostgres) {
  Write-Host "== Download PostgreSQL $PostgresUrl =="
  $null = Save-Url $PostgresUrl $officialZip
  $actual = Get-Sha256 $officialZip
  if ($actual -ne $PostgresSha256) {
    Remove-Item $officialZip -Force
    throw "Контрольная сумма PostgreSQL не совпала с EDB ($PostgresSha256). Скачан файл $actual."
  }
}
if (-not (Test-ZipEntry $officialZip "pgsql/bin/initdb.exe")) {
  throw "В архиве PostgreSQL нет pgsql/bin/initdb.exe."
}
Write-Host "== Pack PostgreSQL runtime (без pgAdmin и StackBuilder) =="
New-PostgresRuntimeZip $officialZip $postgresZip
if (-not (Test-ZipEntry $postgresZip "pgsql/bin/initdb.exe")) {
  throw "В собранном архиве PostgreSQL нет pgsql/bin/initdb.exe."
}
if (Test-ZipEntry $postgresZip "pgsql/pgAdmin 4/runtime/pgAdmin4.exe") {
  throw "В установщик попал pgAdmin. Нужны только bin, lib, share и include."
}

$haveRedist = $false
if (Test-Path $vcRedist) {
  $head = [System.IO.File]::ReadAllBytes($vcRedist)[0..1]
  $haveRedist = ($head[0] -eq 0x4D -and $head[1] -eq 0x5A -and (Get-Item $vcRedist).Length -gt 1MB)
}
if (-not $haveRedist) {
  Write-Host "== Download Visual C++ Redistributable =="
  $effective = Save-Url $VcRedistUrl $vcRedist
  $head = [System.IO.File]::ReadAllBytes($vcRedist)[0..1]
  if ($head[0] -ne 0x4D -or $head[1] -ne 0x5A -or (Get-Item $vcRedist).Length -lt 1MB) {
    Remove-Item $vcRedist -Force
    throw "vc_redist.x64.exe не похож на программу Windows."
  }
  if ($effective -match "/([0-9A-Fa-f]{64})/") {
    $expectedVc = $Matches[1].ToLowerInvariant()
    $actualVc = Get-Sha256 $vcRedist
    if ($actualVc -ne $expectedVc) {
      Remove-Item $vcRedist -Force
      throw "Контрольная сумма Visual C++ не совпала со ссылкой Microsoft."
    }
  }
}

Write-Host "== Publish Setup EXE =="
$setupOut = Join-Path $stage "setup"
dotnet publish $installerProj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:Version=$Version `
  -p:InformationalVersion=$Version `
  -o $setupOut
if ($LASTEXITCODE -ne 0) { throw "Сборка установщика не удалась" }

$built = Join-Path $setupOut "ShiftClub.Server.Setup.exe"
if (-not (Test-Path $built)) { throw "Не найден $built" }
Copy-Item $built $finalExe -Force
Copy-Item $built (Join-Path $outDir "ShiftClub.Server.Setup.exe") -Force

$sizeMb = [math]::Round((Get-Item $finalExe).Length / 1MB, 1)
Write-Host ""
Write-Host "Установщик готов:"
Write-Host "  $finalExe"
Write-Host "  Размер: $sizeMb MB"
Write-Host "Внутри: сервер, .NET, PostgreSQL 16 и Visual C++."
Write-Host "Запустите его на сервере клуба от имени администратора."
