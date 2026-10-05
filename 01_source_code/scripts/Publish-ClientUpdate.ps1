<#
.SYNOPSIS
  Publishes Shell+Updater as a versioned zip and registers it on the API server packages folder
  (and optionally uploads via HTTP).

.EXAMPLE
  .\scripts\Publish-ClientUpdate.ps1 -Version 0.4.1 -ReleaseNotes "Shell timer fix"

.EXAMPLE
  .\scripts\Publish-ClientUpdate.ps1 -Version 0.4.1 -Upload -ApiUrl http://192.168.1.10:5080 -Token <jwt>

.NOTES
  Адрес сервера и пути клубных дисков берутся из scripts\deploy.config.json
  (образец — deploy.config.example.json) или из переменных окружения. Раньше они
  были вписаны в скрипт, и собранный пакет одного клуба указывал на сервер другого.
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$Version,

  [string]$ReleaseNotes = "",
  [string]$Channel = "stable",
  [string]$RepoRoot = "",
  [string]$PackagesDir = "",
  [switch]$Upload,
  [switch]$ClubDeploy,
  [string]$ApiUrl = "",
  [string]$Token = "",
  [string[]]$ClubShellDirs = @(),
  [string]$ClubPackagesDir = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot '_DeployConfig.ps1')
$deploy = Get-ShiftClubDeployConfig -ScriptRoot $PSScriptRoot
Use-ShiftClubDotnet -Config $deploy

# Адрес попадает внутрь пакета Shell, поэтому нужен всегда, а не только для -Upload.
$ApiUrl = Resolve-ShiftClubServerUrl -Provided $ApiUrl -Config $deploy
if (-not $Token) { $Token = $deploy.ApiToken }
if ($ClubShellDirs.Count -eq 0) { $ClubShellDirs = @($deploy.ShellDirs) }
if (-not $ClubPackagesDir) {
  $ClubPackagesDir = if ($deploy.PackagesDir) { $deploy.PackagesDir } else { Join-Path $deploy.ServerDir 'data\client-updates' }
}

if (-not $RepoRoot) {
  if ($PSScriptRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
  } else {
    $RepoRoot = (Resolve-Path (Join-Path (Get-Location) ".")).Path
  }
}

if (-not $PackagesDir) {
  # Club packages stay on C: runtime Server — never pile zips into the source tree / D:
  if (Test-Path $ClubPackagesDir) {
    $PackagesDir = $ClubPackagesDir
  } else {
    $PackagesDir = Join-Path $RepoRoot "src\ShiftClub.Server\data\client-updates"
  }
}

$stage = Join-Path $RepoRoot "dist\client-update-stage\$Version"
$shellOut = Join-Path $stage "shell"
$updaterOut = Join-Path $stage "updater"

Write-Host "== Sync ClientVersionInfo $Version =="
$versionFile = Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Shell\ClientVersionInfo.cs"
if (-not (Test-Path $versionFile)) { throw "Missing $versionFile" }
$versionSrc = Get-Content $versionFile -Raw
$versionSrc = [regex]::Replace(
  $versionSrc,
  'public const string Version = "[^"]+";',
  "public const string Version = `"$Version`";"
)
Set-Content -Path $versionFile -Value $versionSrc -Encoding UTF8 -NoNewline

$csproj = Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Shell\ShiftClub.Client.Shell.csproj"
$proj = Get-Content $csproj -Raw
if ($proj -notmatch '<Version>') {
  $proj = $proj -replace '(<PropertyGroup>\s*)', "`$1`r`n    <Version>$Version</Version>`r`n    <InformationalVersion>$Version</InformationalVersion>`r`n"
} else {
  $proj = [regex]::Replace($proj, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
  $proj = [regex]::Replace($proj, '<InformationalVersion>[^<]*</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>")
}
Set-Content -Path $csproj -Value $proj -Encoding UTF8

Write-Host "== Build Shell $Version (self-contained win-x64) =="
# Framework-dependent updates break PCs without .NET Desktop Runtime
# ("You must install or update .NET to run this application").
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $shellOut | Out-Null
New-Item -ItemType Directory -Force -Path $updaterOut | Out-Null

dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Shell\ShiftClub.Client.Shell.csproj") `
  -c Release -r win-x64 -o $shellOut --self-contained true `
  -p:Version=$Version -p:InformationalVersion=$Version -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw "Shell publish failed" }

Write-Host "== Build Updater (single-file, self-contained) =="
dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Updater\ShiftClub.Client.Updater.csproj") `
  -c Release -r win-x64 -o $updaterOut --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Updater publish failed" }

# Only Updater exe — never overwrite Shell/WPF assemblies (e.g. WindowsBase.dll).
$updaterExe = Join-Path $updaterOut "ShiftClub.Client.Updater.exe"
if (-not (Test-Path $updaterExe)) { throw "Updater exe missing in package stage" }
Copy-Item $updaterExe $shellOut -Force
if (-not (Test-Path (Join-Path $shellOut "ShiftClub.Client.Updater.exe"))) {
  throw "Updater exe missing in package stage"
}

Write-Host "== Build Client.Service (watchdog) =="
$serviceOut = Join-Path $shellOut "service"
New-Item -ItemType Directory -Force -Path $serviceOut | Out-Null
dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Service\ShiftClub.Client.Service.csproj") `
  -c Release -r win-x64 -o $serviceOut --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Service publish failed" }
# Служба-сторож запускает Shell по этому пути: он должен совпадать с тем, куда
# Shell раскладывают на клиентском диске.
$shellInstallDir = if ($ClubShellDirs.Count -gt 0) { $ClubShellDirs[0] } else { 'C:\ShiftClub\Shell' }
@{
  Server = @{ BaseUrl = $ApiUrl }
  Shell  = @{ ExePath = Join-Path $shellInstallDir 'ShiftClub.Client.Shell.exe' }
} | ConvertTo-Json | Set-Content (Join-Path $serviceOut "appsettings.json") -Encoding UTF8

# Адрес клубного сервера — внутрь пакета Shell: обновление не должно увести
# клиентов на чужой или на тестовый сервер.
@{
  Server = @{ BaseUrl = $ApiUrl }
} | ConvertTo-Json | Set-Content (Join-Path $shellOut "appsettings.json") -Encoding UTF8

Write-Host "== Build Client.Keeper (session watchdog) =="
$keeperOut = Join-Path $env:TEMP "ShiftClubKeeperBuild"
if (Test-Path $keeperOut) { Remove-Item $keeperOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $keeperOut | Out-Null
dotnet publish (Join-Path $RepoRoot "src\ShiftClub.Client\ShiftClub.Client.Keeper\ShiftClub.Client.Keeper.csproj") `
  -c Release -r win-x64 -o $keeperOut --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Keeper publish failed" }
Copy-Item (Join-Path $keeperOut "ShiftClub.Client.Keeper.exe") $shellOut -Force

New-Item -ItemType Directory -Force -Path $PackagesDir | Out-Null
$zipName = "ShiftClub.Client.Shell-$Version.zip"
$zipPath = Join-Path $PackagesDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($shellOut, $zipPath)

$sha = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$manifest = [ordered]@{
  version       = $Version
  channel       = $Channel
  packageFile   = $zipName
  sha256        = $sha
  minVersion    = $null
  releaseNotes  = $ReleaseNotes
  publishedAt   = (Get-Date).ToUniversalTime().ToString("o")
}
$manifestPath = Join-Path $PackagesDir "manifest.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

# Keep bin output folders in sync (dotnet exe content root often points there)
foreach ($binRoot in @(
    (Join-Path $RepoRoot "src\ShiftClub.Server\bin\Debug\net8.0\data\client-updates"),
    (Join-Path $RepoRoot "src\ShiftClub.Server\bin\Release\net8.0\data\client-updates")
  )) {
  if (Test-Path (Split-Path $binRoot -Parent)) {
    New-Item -ItemType Directory -Force -Path $binRoot | Out-Null
    Copy-Item $zipPath $binRoot -Force
    Copy-Item $manifestPath $binRoot -Force
    Write-Host "Synced: $binRoot"
  }
}

Write-Host "Published package:"
Write-Host "  $zipPath"
Write-Host "  SHA256: $sha"
Write-Host "  Manifest: $manifestPath"

if ($ClubDeploy) {
  Write-Host "== Club deploy =="
  New-Item -ItemType Directory -Force -Path $ClubPackagesDir | Out-Null
  # PackagesDir may already be ClubPackagesDir — skip self-copy
  $zipFull = [System.IO.Path]::GetFullPath($zipPath)
  $clubZip = [System.IO.Path]::GetFullPath((Join-Path $ClubPackagesDir (Split-Path $zipPath -Leaf)))
  if (-not [string]::Equals($zipFull, $clubZip, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item $zipPath $ClubPackagesDir -Force
    Copy-Item $manifestPath $ClubPackagesDir -Force
  } else {
    Copy-Item $manifestPath $ClubPackagesDir -Force -ErrorAction SilentlyContinue
  }
  Write-Host "Packages -> $ClubPackagesDir"

  if ($ClubShellDirs.Count -eq 0) {
    Write-Warning 'Не заданы папки Shell (shellDirs в deploy.config.json) — файлы на клиентский диск не раскладываем'
  }

  foreach ($dir in $ClubShellDirs) {
    if (-not $dir) { continue }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    # Stop running shell briefly is caller's job; copy files over.
    Get-ChildItem $shellOut -Force | ForEach-Object {
      $dest = Join-Path $dir $_.Name
      if ($_.PSIsContainer) {
        Copy-Item $_.FullName $dest -Recurse -Force
      } else {
        Copy-Item $_.FullName $dest -Force
      }
    }
    @{
      Server = @{ BaseUrl = $ApiUrl }
    } | ConvertTo-Json | Set-Content (Join-Path $dir "appsettings.json") -Encoding UTF8
    # Never leave debug symbols on Games disk
    Get-ChildItem $dir -Filter '*.pdb' -Recurse -Force -EA SilentlyContinue | Remove-Item -Force -EA SilentlyContinue
    Write-Host "Shell files -> $dir"
  }

  Write-Host "NOTE: ShiftClubClient service must run on CLIENT PCs (in VHD), not on the CCBoot server."
}

if ($Upload) {
  if (-not $Token) { throw "Specify -Token for -Upload" }
  $form = @{
    version      = $Version
    releaseNotes = $ReleaseNotes
    channel      = $Channel
    package      = Get-Item $zipPath
  }
  Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/client-updates/publish" `
    -Headers @{ Authorization = "Bearer $Token" } `
    -Form $form | ConvertTo-Json -Depth 5
  Write-Host "Uploaded to $ApiUrl"
}

Write-Host "Done. Clients auto-check every 5 min, or send UpdateClient from floor map."
