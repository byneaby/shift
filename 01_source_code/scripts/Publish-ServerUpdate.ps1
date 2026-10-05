<#
.SYNOPSIS
  Собирает выпуск сервера (API + панель) в zip и пишет манифест канала обновлений.
  Запускается у поставщика, не в клубе.

.DESCRIPTION
  На выходе в папке канала появляются два файла:
    ShiftClub.Server-<версия>.zip
    manifest.json
  Клубный сервер ждёт их по адресу <FeedUrl>/server/<канал>/manifest.json —
  то есть папку канала нужно разложить так же: /server/stable/...

  В пакет НЕ попадают appsettings.*.json, secrets.env и папка data: это
  настройки и данные клуба, их апдейтер сохраняет на месте.

.EXAMPLE
  .\scripts\Publish-ServerUpdate.ps1 -Version 0.7.0 -ReleaseNotes "Лицензии, копии базы, мастер запуска"

.EXAMPLE
  .\scripts\Publish-ServerUpdate.ps1 -Version 0.7.0 -MinVersion 0.6.0 -FeedDir D:\Releases\server\stable
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$Version,

  [string]$ReleaseNotes = "",
  [string]$Channel = "stable",

  # С версий ниже этой обновляться нельзя — клубу сначала нужен промежуточный выпуск.
  [string]$MinVersion = "",

  [string]$RepoRoot = "",
  [string]$FeedDir = "",

  # Пропустить сборку панели, если dist уже собран.
  [switch]$SkipWeb
)

$ErrorActionPreference = "Stop"

if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
  throw "Версия должна быть вида 1.2.3, получено '$Version'"
}
if ($MinVersion -and -not ($MinVersion -match '^\d+\.\d+\.\d+$')) {
  throw "MinVersion должна быть вида 1.2.3, получено '$MinVersion'"
}

if (-not $RepoRoot) {
  $RepoRoot = if ($PSScriptRoot) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Get-Location).Path }
}
if (-not $FeedDir) {
  $FeedDir = Join-Path $RepoRoot "dist\feed\server\$Channel"
}

$stage = Join-Path $RepoRoot "dist\server-update-stage\$Version"
$serverProject = Join-Path $RepoRoot "src\ShiftClub.Server\ShiftClub.Server.csproj"
$webDir = Join-Path $RepoRoot "src\ShiftClub.Web"

if (-not (Test-Path $serverProject)) { throw "Не найден $serverProject" }

Write-Host "== Панель (SPA) =="
if ($SkipWeb) {
  Write-Host "пропущено (-SkipWeb)"
} else {
  Push-Location $webDir
  try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci завершился с кодом $LASTEXITCODE" }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build завершился с кодом $LASTEXITCODE" }
  } finally {
    Pop-Location
  }
}
$webDist = Join-Path $webDir "dist"
if (-not (Test-Path (Join-Path $webDist "index.html"))) {
  throw "Панель не собрана: нет $webDist\index.html"
}

Write-Host "== Сервер (self-contained win-x64) =="
# Self-contained, чтобы обновление не падало на клубах без нужного .NET Runtime.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

& dotnet publish $serverProject `
  -c Release -r win-x64 -o $stage --self-contained true `
  -p:Version=$Version -p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }

Write-Host "== Чистка пакета =="
# Настройки и данные клуба в пакет попадать не должны: апдейтер оставляет
# существующие на месте, а эти файлы их бы перезаписали.
foreach ($pattern in @('appsettings.Development.json', 'appsettings.Production.json', 'secrets.env', '*.pdb')) {
  Get-ChildItem $stage -Filter $pattern -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Force
}
foreach ($folder in @('data', 'logs')) {
  $path = Join-Path $stage $folder
  if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

# Панель — в wwwroot пакета, рядом со статикой публичных страниц из репозитория.
$wwwroot = Join-Path $stage "wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item (Join-Path $webDist '*') $wwwroot -Recurse -Force

# Скрипты запуска и обновления едут вместе с сервером: апдейтер должен
# обновлять и сам себя, иначе исправления в нём до клубов не доходят.
foreach ($script in @('Start-ShiftClubApi.ps1', 'Watch-ShiftClubApi.ps1', 'Update-ShiftClubServer.ps1')) {
  $src = Join-Path $PSScriptRoot $script
  if (Test-Path $src) { Copy-Item $src $stage -Force } else { Write-Warning "Нет скрипта $script" }
}

Write-Host "== Упаковка =="
New-Item -ItemType Directory -Force -Path $FeedDir | Out-Null
$zipName = "ShiftClub.Server-$Version.zip"
$zipPath = Join-Path $FeedDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)

$sha = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$size = (Get-Item $zipPath).Length

$minVersionValue = $null
if ($MinVersion) { $minVersionValue = $MinVersion }

$manifest = [ordered]@{
  version      = $Version
  channel      = $Channel
  packageFile  = $zipName
  sha256       = $sha
  sizeBytes    = $size
  minVersion   = $minVersionValue
  releaseNotes = $ReleaseNotes
  publishedAt  = (Get-Date).ToUniversalTime().ToString("o")
}
$manifestPath = Join-Path $FeedDir "manifest.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "Выпуск готов:"
Write-Host "  пакет:    $zipPath"
Write-Host "  размер:   $([Math]::Round($size / 1MB, 1)) МБ"
Write-Host "  SHA256:   $sha"
Write-Host "  манифест: $manifestPath"
Write-Host ""
Write-Host "Выложите папку канала так, чтобы она открывалась по адресу"
Write-Host "  <FeedUrl>/server/$Channel/manifest.json"
Write-Host "и пропишите клубам FeedUrl в ServerUpdates:FeedUrl."
