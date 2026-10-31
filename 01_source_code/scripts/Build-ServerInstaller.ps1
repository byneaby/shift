<#
.SYNOPSIS
  Собирает один EXE установщика сервера SHIFT Club с мастером настройки.

.EXAMPLE
  .\scripts\Build-ServerInstaller.ps1

Нужен .NET SDK 8 x64 (не только runtime). Путь к dotnet берётся из PATH,
отдельную папку D:\Dev\dotnet указывать не нужно.
#>
param(
  [string]$Version = "1.0.0",
  [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

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
Write-Host "Запустите его на сервере клуба от имени администратора."
