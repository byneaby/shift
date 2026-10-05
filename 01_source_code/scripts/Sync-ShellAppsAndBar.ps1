#Requires -Version 5.1
param(
  [string]$ApiBase = 'http://192.168.1.250:5080',
  [string]$JsonPath = (Join-Path $PSScriptRoot 'shell-apps-import.json'),
  [string]$SecretsPath = 'C:\ShiftClub\Server\secrets.env'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Read-Secrets([string]$Path) {
  $map = @{}
  Get-Content -LiteralPath $Path | ForEach-Object {
    if ($_ -match '^\s*#' -or $_ -notmatch '=') { return }
    $i = $_.IndexOf('=')
    $map[$_.Substring(0, $i).Trim()] = $_.Substring($i + 1).Trim()
  }
  $map
}

$secrets = Read-Secrets $SecretsPath
$loginResp = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/auth/login" -ContentType 'application/json' -Body (@{
  login = $secrets['Seed__OwnerLogin']
  password = $secrets['Seed__OwnerPassword']
} | ConvertTo-Json)
$token = $loginResp.data.accessToken
if (-not $token) { throw 'No access token' }
$hdr = @{ Authorization = "Bearer $token" }
$jsonHdr = @{
  Authorization  = "Bearer $token"
  'Content-Type' = 'application/json; charset=utf-8'
}

$payload = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$list = Invoke-RestMethod -Method Get -Uri "$ApiBase/api/software-apps?includeInactive=true" -Headers $hdr
$byName = @{}
foreach ($a in @($list.data)) { $byName[[string]$a.name] = $a }

$rename = @{ 'FACEIT' = 'FACEIT AC'; 'PUBG' = 'PUBG BATTLEGROUNDS' }
foreach ($old in @($rename.Keys)) {
  $new = $rename[$old]
  if ($byName.ContainsKey($old) -and -not $byName.ContainsKey($new)) {
    Write-Host "Map rename $old -> $new"
    $byName[$new] = $byName[$old]
  }
}

$created = 0; $updated = 0
foreach ($app in $payload.apps) {
  $icon = $null
  if ($byName.ContainsKey([string]$app.name) -and $byName[[string]$app.name].iconPath) {
    $icon = $byName[[string]$app.name].iconPath
  }
  $bodyObj = [ordered]@{
    name             = [string]$app.name
    category         = [string]$app.category
    exePath          = [string]$app.exePath
    arguments        = $app.arguments
    workingDirectory = $app.workingDirectory
    iconPath         = $icon
    sortOrder        = [int]$app.sortOrder
    isActive         = $true
    minAge           = $app.minAge
  }
  $json = $bodyObj | ConvertTo-Json -Depth 5
  if ($byName.ContainsKey([string]$app.name)) {
    $id = $byName[[string]$app.name].id
    Invoke-RestMethod -Method Put -Uri "$ApiBase/api/software-apps/$id" -Headers $jsonHdr -Body $json | Out-Null
    $updated++
    Write-Host "UPD $($app.name)"
  } else {
    Invoke-RestMethod -Method Post -Uri "$ApiBase/api/software-apps" -Headers $jsonHdr -Body $json | Out-Null
    $created++
    Write-Host "ADD $($app.name)"
  }
}

$reload = Invoke-RestMethod -Method Get -Uri "$ApiBase/api/software-apps?includeInactive=true" -Headers $hdr
foreach ($a in @($reload.data)) {
  if (($a.name -eq 'FACEIT' -or $a.name -eq 'PUBG') -and $a.isActive) {
    $bodyObj = [ordered]@{
      name = $a.name; category = $a.category; exePath = $a.exePath
      arguments = $a.arguments; workingDirectory = $a.workingDirectory
      iconPath = $a.iconPath; sortOrder = [int]$a.sortOrder; isActive = $false; minAge = $a.minAge
    }
    Invoke-RestMethod -Method Put -Uri "$ApiBase/api/software-apps/$($a.id)" -Headers $jsonHdr -Body ($bodyObj | ConvertTo-Json) | Out-Null
    Write-Host "OFF $($a.name)"
  }
}
Write-Host "Apps created=$created updated=$updated"

$cats = Invoke-RestMethod -Uri "$ApiBase/api/bar/categories" -Headers $hdr
$energyId = ($cats.data | Where-Object { $_.code -eq 'ENERGY' }).id
$drinksId = ($cats.data | Where-Object { $_.code -eq 'DRINKS' }).id
$hookahId = ($cats.data | Where-Object { $_.code -eq 'HOOKAH' }).id
if (-not $hookahId) {
  $created = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/bar/categories" -Headers $jsonHdr -Body (@{
    name = 'Кальяны'; code = 'HOOKAH'; sortOrder = 4
  } | ConvertTo-Json)
  $hookahId = $created.data.id
  Write-Host "BAR CAT ADD Кальяны"
}
$prods = Invoke-RestMethod -Uri "$ApiBase/api/bar/products?includeInactive=true" -Headers $hdr
$bySku = @{}; $byPName = @{}
foreach ($p in @($prods.data)) { $bySku[$p.sku] = $p; $byPName[$p.name] = $p }

function Upsert-BarProduct {
  param($Name, $Sku, $CategoryId, [decimal]$Sale, [decimal]$Cost)
  $unitDefault = 'pcs'
  if ($bySku.ContainsKey($Sku)) {
    $p = $bySku[$Sku]
    $unit = if ($p.unit) { $p.unit } else { $unitDefault }
    $body = (@{
      categoryId = $CategoryId; name = $Name; sku = $Sku; barcode = $p.barcode
      unit = $unit; costPrice = $Cost; salePrice = $Sale
      minStockQty = $p.minStockQty; isActive = $true; imageUrl = $p.imageUrl
    } | ConvertTo-Json)
    Invoke-RestMethod -Method Put -Uri "$ApiBase/api/bar/products/$($p.id)" -Headers $jsonHdr -Body $body | Out-Null
    Write-Host "BAR UPD $Name $Sale"
  } elseif ($byPName.ContainsKey($Name)) {
    $p = $byPName[$Name]
    $unit = if ($p.unit) { $p.unit } else { $unitDefault }
    $body = (@{
      categoryId = $CategoryId; name = $Name; sku = $Sku; barcode = $p.barcode
      unit = $unit; costPrice = $Cost; salePrice = $Sale
      minStockQty = $p.minStockQty; isActive = $true; imageUrl = $p.imageUrl
    } | ConvertTo-Json)
    Invoke-RestMethod -Method Put -Uri "$ApiBase/api/bar/products/$($p.id)" -Headers $jsonHdr -Body $body | Out-Null
    Write-Host "BAR UPD-name $Name $Sale"
  } else {
    $body = (@{
      categoryId = $CategoryId; name = $Name; sku = $Sku; barcode = $null
      unit = $unitDefault; costPrice = $Cost; salePrice = $Sale
      initialStock = 0; minStockQty = 0; imageUrl = $null
    } | ConvertTo-Json)
    Invoke-RestMethod -Method Post -Uri "$ApiBase/api/bar/products" -Headers $jsonHdr -Body $body | Out-Null
    Write-Host "BAR ADD $Name $Sale"
  }
}

$L = [char]0x043B
$maxiTea = 'Maxi' + [char]0x0427 + [char]0x0430 + [char]0x0439 + ' 1' + $L

Upsert-BarProduct 'Gorilla Energy' 'GORILLA' $energyId 800 400
Upsert-BarProduct 'Red Bull' 'RBULL' $energyId 1000 500
Upsert-BarProduct ('CocaCola 1' + $L) 'COLA1L' $drinksId 800 400
Upsert-BarProduct ('CocaCola 0.5' + $L) 'COLA05' $drinksId 600 300
Upsert-BarProduct ('FuseTea 1' + $L) 'FUSE1L' $drinksId 800 400
Upsert-BarProduct ('FuseTea 0.5' + $L) 'FUSE05' $drinksId 600 300
Upsert-BarProduct ('AVA Lemonade 1' + $L) 'AVA1L' $drinksId 800 400
Upsert-BarProduct $maxiTea 'MAXITEA1L' $drinksId 800 400

Upsert-BarProduct 'Кальян лайт' 'HOOKAH_LIGHT' $hookahId 3500 0
Upsert-BarProduct 'Кальян хард' 'HOOKAH_HARD' $hookahId 6500 0
Upsert-BarProduct 'Замена чаши' 'HOOKAH_BOWL' $hookahId 2000 0

$oldCola = @($prods.data) | Where-Object { $_.sku -eq 'COLA033' } | Select-Object -First 1
if ($oldCola -and $oldCola.isActive) {
  $body = (@{
    categoryId = $oldCola.categoryId; name = $oldCola.name; sku = $oldCola.sku; barcode = $oldCola.barcode
    unit = $oldCola.unit; costPrice = $oldCola.costPrice; salePrice = $oldCola.salePrice
    minStockQty = $oldCola.minStockQty; isActive = $false; imageUrl = $oldCola.imageUrl
  } | ConvertTo-Json)
  Invoke-RestMethod -Method Put -Uri "$ApiBase/api/bar/products/$($oldCola.id)" -Headers $jsonHdr -Body $body | Out-Null
  Write-Host "BAR OFF $($oldCola.name)"
}

Write-Host '--- verify apps ---'
$final = Invoke-RestMethod -Uri "$ApiBase/api/software-apps" -Headers $hdr
@('Battlefield 1','PUBG MOBILE','Counter-Strike 1.6','Grand Theft Auto San Andreas','MineCraft','FACEIT AC','Discord','BlueStacks 5','Left 4 Dead 2 Server','Left 4 Dead 2','TeamSpeak','EXCEL','POWERPOINT','WORD','Counter-Strike 2','Counter-Strike 1.6 Steam','PUBG BATTLEGROUNDS','Apex Legends','R.E.P.O.','Dota 2') | ForEach-Object {
  $hit = @($final.data) | Where-Object name -EQ $_ | Select-Object -First 1
  if ($hit) { "OK $_ => $($hit.exePath)" } else { "MISSING $_" }
}
Write-Host '--- verify bar ---'
$bp = Invoke-RestMethod -Uri "$ApiBase/api/bar/products" -Headers $hdr
@($bp.data) | ForEach-Object { "$($_.name) | $($_.salePrice)" }
