# Deploy staff SPA to C:\ShiftClub\Server\wwwroot WITHOUT wiping public site assets.
# Preserves: site, price, promo, tg-webapp, media
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$webDir = Join-Path $repoRoot 'src\ShiftClub.Web'
$dist = Join-Path $webDir 'dist'
$serverWww = 'C:\ShiftClub\Server\wwwroot'
$serverWwwMirror = 'C:\ShiftClub\Server\www'
$srcStatic = Join-Path $repoRoot 'src\ShiftClub.Server\wwwroot'

function Deploy-SpaTo([string]$dest) {
  if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }

  # Remove only SPA root files / assets — keep public folders
  $preserve = @('site', 'price', 'promo', 'tg-webapp', 'product', 'work', 'media', 'uploads')
  Get-ChildItem $dest -Force | Where-Object { $preserve -notcontains $_.Name } | Remove-Item -Recurse -Force

  Copy-Item (Join-Path $dist '*') $dest -Recurse -Force

  # Overlay public static folders from source so existing marketing pages stay,
  # and new/updated files (e.g. /site/start.html) also get deployed.
  foreach ($folder in @('site', 'price', 'promo', 'tg-webapp', 'product', 'work')) {
    $src = Join-Path $srcStatic $folder
    $dst = Join-Path $dest $folder
    if (Test-Path $src) {
      if (-not (Test-Path $dst)) {
        Write-Host "Restoring missing $folder -> $dst"
        New-Item -ItemType Directory -Path $dst | Out-Null
      } else {
        Write-Host "Syncing static $folder -> $dst"
      }
      # Mirror files (not nested folder copy) so updates always overwrite
      & robocopy $src $dst /E /IS /IT /NFL /NDL /NJH /NJS /NP | Out-Null
      if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $folder (code $LASTEXITCODE)" }
    }
  }
}

Push-Location $webDir
try {
  npm run build
} finally {
  Pop-Location
}

if (-not (Test-Path (Join-Path $dist 'index.html'))) {
  throw "Web build failed: $dist\index.html missing"
}

Deploy-SpaTo $serverWww
if (Test-Path $serverWwwMirror) {
  Deploy-SpaTo $serverWwwMirror
}

$rootIndex = Join-Path $serverWww 'index.html'
$distIndex = Join-Path $dist 'index.html'
if ((Test-Path $rootIndex) -and (Test-Path $distIndex)) {
  $rootHash = (Get-FileHash $rootIndex -Algorithm SHA256).Hash
  $distHash = (Get-FileHash $distIndex -Algorithm SHA256).Hash
  if ($rootHash -ne $distHash) {
    Write-Warning 'wwwroot index.html differs from dist - forcing copy'
    Copy-Item $distIndex $rootIndex -Force
    if (Test-Path $serverWwwMirror) {
      Copy-Item $distIndex (Join-Path $serverWwwMirror 'index.html') -Force
    }
  }
}

Write-Host "SPA deployed. Preserved site/price/promo/tg-webapp/product/work."
@(
  '/health',
  '/site/hire.html',
  '/site/index.html',
  '/site/start.html',
  '/start',
  '/product',
  '/work',
  '/api/hiring/schema',
  '/login'
) | ForEach-Object {
  try {
    $r = Invoke-WebRequest "http://127.0.0.1:5080$_" -UseBasicParsing -TimeoutSec 5
    "OK $_ => $($r.StatusCode)"
  } catch {
    $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'ERR' }
    "FAIL $_ => $code"
  }
}
