#Requires -Version 5.1
<#
.SYNOPSIS
  Import Shell games + attach covers (Steam CDN / local / exe-icon tiles).
#>
param(
  [string]$ApiBase = 'http://192.168.1.250:5080',
  [string]$JsonPath = (Join-Path $PSScriptRoot 'shell-apps-import.json'),
  [string]$SecretsPath = 'C:\ShiftClub\Server\secrets.env',
  [string]$CoverCache = (Join-Path $PSScriptRoot '_cover-cache')
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Drawing

# Steam library capsule (official). Name -> appid
$SteamIds = @{
  'Counter-Strike 2'           = 730
  'Dota 2'                     = 570
  'PUBG'                       = 578080
  'PUBG BATTLEGROUNDS'         = 578080
  'Rust'                       = 252490
  'R.E.P.O.'                   = 3241660
  'Apex Legends'               = 1172470
  'World of Tanks'             = 1407200
  'Tanki Online'               = 562010
  'Grand Theft Auto V'         = 271590
  'Grand Theft Auto San Andreas' = 12120
  'Battlefield 1'              = 1238840
  'The Witcher 3'              = 292030
  'Cyberpunk 2077'             = 1091500
  'Left 4 Dead 2'              = 550
  'Need for Speed Most Wanted' = 1262560
  'Far Cry Primal'             = 371660
  'Mafia III'                  = 360430
  'Mafia II'                   = 50130
  'Call of Duty 4'             = 7940
  'Warcraft III'               = 973760
  'Counter-Strike 1.6'         = 10
  'Counter-Strike 1.6 Steam'   = 10
  'Blur'                       = 263280
  'FlatOut 2'                  = 2990
  'Serious Sam 2'              = 41010
  'Sniper Ghost Warrior 2'     = 34830
  'Command & Conquer Generals' = 2229870
  'Command & Conquer Zero Hour'= 2229880
}

# Direct URLs (verified official-style art)
$DirectUrls = @{
  'VALORANT'          = 'https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co2mvt.jpg'
  'League of Legends' = 'https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co49wj.jpg'
  'Fortnite'          = 'https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co2ekt.jpg'
}

# Exe icons -> branded portrait tiles for launchers / SC2
$IconExes = @{
  'Steam'              = 'F:\Steam\steam.exe'
  'Epic Games'         = 'F:\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe'
  'Riot Client'        = 'F:\Riot Games\Riot Client\RiotClientServices.exe'
  'Battle.net'         = 'F:\Battle.net\Battle.net.exe'
  'Mail.ru GameCenter' = 'D:\GameCenter\GameCenter.exe'
  'FACEIT'             = 'D:\FACEIT\FACEIT.exe'
  'StarCraft II'       = 'F:\Battle.net\StarCraft II\StarCraft II.exe'
}

function Read-Secrets([string]$Path) {
  $map = @{}
  Get-Content -LiteralPath $Path | ForEach-Object {
    if ($_ -match '^\s*#' -or $_ -notmatch '=') { return }
    $i = $_.IndexOf('=')
    $map[$_.Substring(0, $i).Trim()] = $_.Substring($i + 1).Trim()
  }
  $map
}

function New-IconCover([string]$ExePath, [string]$Title, [string]$OutPath) {
  $ico = [System.Drawing.Icon]::ExtractAssociatedIcon($ExePath)
  if (-not $ico) { throw "No icon: $ExePath" }
  $bmp = $ico.ToBitmap()
  $width = 600
  $height = 900
  $canvas = New-Object System.Drawing.Bitmap($width, $height)
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $bg = [System.Drawing.Color]::FromArgb(18, 22, 30)
  $g.Clear($bg)
  $iconSize = 280
  $x = [int](($width - $iconSize) / 2)
  $y = 220
  $g.DrawImage($bmp, $x, $y, $iconSize, $iconSize)

  $font = New-Object System.Drawing.Font('Segoe UI', 26, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Point)
  $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
  $sf = New-Object System.Drawing.StringFormat
  $sf.Alignment = [System.Drawing.StringAlignment]::Center
  $sf.LineAlignment = [System.Drawing.StringAlignment]::Near
  $rect = New-Object System.Drawing.RectangleF(40, 560, ($width - 80), 280)
  $g.DrawString($Title, $font, $brush, $rect, $sf)

  $canvas.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
  $g.Dispose(); $canvas.Dispose(); $bmp.Dispose(); $ico.Dispose(); $font.Dispose(); $brush.Dispose(); $sf.Dispose()
}

function Get-SteamCover([int]$AppId, [string]$Dest) {
  $urls = @(
    "https://cdn.cloudflare.steamstatic.com/steam/apps/$AppId/library_600x900.jpg",
    "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/$AppId/library_600x900.jpg",
    "https://cdn.cloudflare.steamstatic.com/steam/apps/$AppId/library_600x900_2x.jpg",
    "https://cdn.cloudflare.steamstatic.com/steam/apps/$AppId/capsule_616x353.jpg"
  )
  foreach ($u in $urls) {
    try {
      Invoke-WebRequest -Uri $u -OutFile $Dest -UseBasicParsing -TimeoutSec 35
      if ((Get-Item -LiteralPath $Dest).Length -gt 3000) { return $true }
    } catch { }
  }
  return $false
}

function Download-Url([string]$Url, [string]$Dest) {
  Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing -TimeoutSec 40 -Headers @{ 'User-Agent' = 'Mozilla/5.0 ShiftClub/1.0' }
  return ((Get-Item -LiteralPath $Dest).Length -gt 3000)
}

function Upload-Cover([string]$ApiBase, [string]$Token, [guid]$Id, [string]$FilePath) {
  $url = "$ApiBase/api/software-apps/$Id/image"
  $raw = & curl.exe -sS -X POST $url -H "Authorization: Bearer $Token" -F "file=@$FilePath;type=image/jpeg"
  if ($LASTEXITCODE -ne 0) { throw "curl failed: $raw" }
  return $raw | ConvertFrom-Json
}

# ---- main ----
New-Item -ItemType Directory -Force -Path $CoverCache | Out-Null
$secrets = Read-Secrets $SecretsPath
$login = $secrets['Seed__OwnerLogin']
$password = $secrets['Seed__OwnerPassword']

Write-Host "Login $ApiBase ..."
$loginResp = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/auth/login" -ContentType 'application/json' -Body (@{ login = $login; password = $password } | ConvertTo-Json)
$token = $loginResp.data.accessToken
if (-not $token) { $token = $loginResp.accessToken }
if (-not $token) { throw ($loginResp | ConvertTo-Json -Depth 6) }
$auth = @{ Authorization = "Bearer $token" }

$payload = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($a in $payload.apps) {
  if (-not (Test-Path -LiteralPath $a.exePath)) { Write-Warning "Missing exe: $($a.name) -> $($a.exePath)" }
}

Write-Host "Import $($payload.apps.Count) apps..."
$importJson = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8
$importResp = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/software-apps/import?updateExisting=true" -Headers $auth -ContentType 'application/json; charset=utf-8' -Body $importJson
Write-Host ($importResp | ConvertTo-Json -Depth 6 -Compress)

$list = Invoke-RestMethod -Method Get -Uri "$ApiBase/api/software-apps" -Headers $auth
$apps = @($list.data)
Write-Host "DB apps: $($apps.Count)"

$ok = 0; $fail = 0
foreach ($app in $apps) {
  $name = [string]$app.name
  $safe = ($name -replace '[^\w\-]+', '_').Trim('_')
  $file = Join-Path $CoverCache "$safe.jpg"
  $got = $false

  try {
    if ($SteamIds.ContainsKey($name)) {
      Write-Host "Steam cover: $name ($($SteamIds[$name]))"
      $got = Get-SteamCover -AppId ([int]$SteamIds[$name]) -Dest $file
    }
    elseif ($DirectUrls.ContainsKey($name)) {
      Write-Host "Direct cover: $name"
      $got = Download-Url -Url $DirectUrls[$name] -Dest $file
    }
    elseif ($IconExes.ContainsKey($name)) {
      Write-Host "Icon tile: $name"
      New-IconCover -ExePath $IconExes[$name] -Title $name -OutPath $file
      $got = Test-Path -LiteralPath $file
    }
    else {
      Write-Host "SKIP (no cover source): $name"
      continue
    }

    if (-not $got) { throw 'download/create failed' }
    $up = Upload-Cover -ApiBase $ApiBase -Token $token -Id ([guid]$app.id) -FilePath $file
    Write-Host "  -> $($up.data.iconPath)"
    $ok++
  }
  catch {
    Write-Warning "Cover fail [$name]: $($_.Exception.Message)"
    $fail++
  }
}

Write-Host "Done covers ok=$ok fail=$fail"
