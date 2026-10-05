<#
.SYNOPSIS
  Local scan of game server disks -> JSON for Shell import.

.EXAMPLE
  .\Scan-GamesToShell.ps1
  .\Scan-GamesToShell.ps1 -Drives D,F
  .\Scan-GamesToShell.ps1 -AllDrives -OutFile C:\Temp\shell-apps-import.json
#>
[CmdletBinding()]
param(
  [string[]]$Drives = @(),
  [switch]$AllDrives,
  [int]$MaxDepth = 3,
  [string]$OutFile = "",
  [string]$Category = "Games"
)

$ErrorActionPreference = "Stop"

if (-not $OutFile) {
  $OutFile = Join-Path (Get-Location) "shell-apps-import.json"
}

$skipExeName = @(
  'uninstall', 'unins00', 'setup', 'install', 'installer', 'update', 'updater',
  'crash', 'crashhandler', 'unitycrashhandler', 'crashpad', 'bugreport',
  'vcredist', 'vc_redist', 'dxsetup', 'directx', 'dotnet', 'redist',
  'easyanticheat', 'beservice', 'battleye', 'eac_launcher',
  'steamerrorreporter', 'cefsharp', 'chrome_elf', 'helper', 'notification_helper',
  'report', 'werfault', 'touchup', 'repair', 'register', 'bootstrapper',
  'privilege_helper', 'elevation_service', 'ipc_service', 'overlay'
)

$skipFolderParts = @(
  '\windows\', '\program files\', '\program files (x86)\', '\programdata\',
  '\windowsapps\', '\$recycle.bin\', '\system volume information\',
  '\writeback', '\cache\', '\image\', '\temp\', '\tmp\'
)

function Test-IsJunkExe([string]$name) {
  $n = $name.ToLowerInvariant()
  foreach ($p in $skipExeName) {
    if ($n.Contains($p)) { return $true }
  }
  return $false
}

function Test-IsSkipPath([string]$path) {
  $p = $path.ToLowerInvariant()
  foreach ($s in $skipFolderParts) {
    if ($p.Contains($s.Trim('\'))) { return $true }
  }
  return $false
}

function Get-ScanDrives {
  $picked = New-Object System.Collections.ArrayList

  if ($Drives -and $Drives.Count -gt 0) {
    foreach ($d in $Drives) {
      $letter = $d.ToString().TrimEnd(':').Trim().ToUpperInvariant()
      [void]$picked.Add($letter + ':')
    }
    return @($picked | Select-Object -Unique)
  }

  $vols = @()
  try {
    $vols = @(Get-Volume -ErrorAction Stop | Where-Object {
      $_.DriveLetter -and $_.Size -gt 1GB
    })
  } catch {
    # Get-Volume missing: fallback to Get-PSDrive
    Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue | Where-Object {
      $_.Used -or $_.Free
    } | ForEach-Object {
      $vols += [pscustomobject]@{
        DriveLetter = $_.Name
        FileSystemLabel = ''
        Size = [int64]($_.Used + $_.Free)
      }
    }
  }

  if ($AllDrives) {
    foreach ($v in $vols) {
      [void]$picked.Add(([string]$v.DriveLetter).ToUpperInvariant() + ':')
    }
    return @($picked | Select-Object -Unique | Sort-Object)
  }

  foreach ($v in $vols) {
    $letter = ([string]$v.DriveLetter).ToUpperInvariant() + ':'
    $label = ''
    try { $label = ([string]$v.FileSystemLabel).ToLowerInvariant() } catch {}
    if ($letter -eq 'C:') { continue }
    if ($label -match 'writeback|cache|image|system|ulan') { continue }
    if ($label -match 'game|online|steam|library|soft|apps') {
      [void]$picked.Add($letter)
      continue
    }
    try {
      if ([int64]$v.Size -gt 100GB) { [void]$picked.Add($letter) }
    } catch {
      [void]$picked.Add($letter)
    }
  }

  if ($picked.Count -eq 0) {
    foreach ($v in $vols) {
      $letter = ([string]$v.DriveLetter).ToUpperInvariant() + ':'
      if ($letter -ne 'C:') { [void]$picked.Add($letter) }
    }
  }

  return @($picked | Select-Object -Unique | Sort-Object)
}

function Get-GameRoots([string]$driveRoot) {
  $roots = New-Object System.Collections.ArrayList
  $known = @(
    'Games', 'Game', 'SteamLibrary', 'Steam', 'Epic Games', 'EpicGames',
    'Online Game', 'Online', 'Library', 'Apps', 'Soft', 'Software',
    'Battle.net', 'Origin', 'Ubisoft', 'Riot Games', 'XboxGames'
  )
  foreach ($k in $known) {
    $p = Join-Path $driveRoot $k
    if (Test-Path -LiteralPath $p) {
      [void]$roots.Add([System.IO.Path]::GetFullPath($p))
    }
  }

  Get-ChildItem -LiteralPath $driveRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    $common = Join-Path $_.FullName 'steamapps\common'
    if (Test-Path -LiteralPath $common) {
      [void]$roots.Add([System.IO.Path]::GetFullPath($common))
    }
  }

  $commonRoot = Join-Path $driveRoot 'steamapps\common'
  if (Test-Path -LiteralPath $commonRoot) {
    [void]$roots.Add([System.IO.Path]::GetFullPath($commonRoot))
  }

  if ($roots.Count -eq 0) {
    Get-ChildItem -LiteralPath $driveRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
      $n = $_.Name.ToLowerInvariant()
      if ($n -match '^(windows|users|program files|program files \(x86\)|programdata|system volume information|\$recycle\.bin|recovery|perflogs)$') {
        return
      }
      [void]$roots.Add($_.FullName)
    }
  }

  return @($roots | Select-Object -Unique)
}

function Get-CandidateFolders([string]$root) {
  $folders = New-Object System.Collections.ArrayList
  $leaf = Split-Path $root -Leaf
  if ($leaf -ieq 'common' -or $leaf -match '^(Games|Game|Online Game|Online|Library|Apps|Soft|Software)$') {
    Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | ForEach-Object {
      [void]$folders.Add($_.FullName)
    }
    return @($folders)
  }
  [void]$folders.Add($root)
  Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
    Select-Object -First 80 |
    ForEach-Object { [void]$folders.Add($_.FullName) }
  return @($folders)
}

function Pick-BestExe([string]$folder) {
  if (Test-IsSkipPath $folder) { return $null }

  $exes = @(Get-ChildItem -LiteralPath $folder -Filter *.exe -File -Recurse -Depth $MaxDepth -ErrorAction SilentlyContinue |
    Where-Object {
      -not (Test-IsJunkExe $_.BaseName) -and
      -not (Test-IsSkipPath $_.FullName) -and
      $_.Length -gt 200KB
    })

  if ($exes.Count -eq 0) { return $null }

  $folderLeaf = (Split-Path $folder -Leaf).ToLowerInvariant() -replace '[^a-z0-9]', ''
  $best = $null
  $bestScore = -1

  foreach ($e in $exes) {
    $base = $e.BaseName.ToLowerInvariant() -replace '[^a-z0-9]', ''
    $score = 0
    if ($base -eq $folderLeaf) { $score += 100 }
    elseif ($folderLeaf -and ($base.StartsWith($folderLeaf) -or $folderLeaf.StartsWith($base))) { $score += 60 }
    elseif ($folderLeaf -and $base.Contains($folderLeaf)) { $score += 40 }

    try {
      $full = $e.FullName
      if ($full.Length -gt $folder.Length -and $full.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase)) {
        $rel = $full.Substring($folder.Length).TrimStart('\')
        $relDepth = if ([string]::IsNullOrEmpty($rel)) { 1 } else { ($rel -split '\\').Count }
        $score += [Math]::Max(0, 20 - ($relDepth * 3))
      }
    } catch {}

    try {
      $mb = [double]$e.Length / 1MB
      $score += [Math]::Min(30, [int]$mb)
    } catch {}

    if ($base -match 'launcher|client|game') { $score += 8 }

    if ($score -gt $bestScore) {
      $bestScore = $score
      $best = $e
    }
  }

  return $best
}

function Guess-Category([string]$path) {
  $p = $path.ToLowerInvariant()
  if ($p -match 'steam') { return 'Steam' }
  if ($p -match 'epic') { return 'Epic' }
  if ($p -match 'online') { return 'Online' }
  if ($p -match 'bootcamp|esport') { return 'Bootcamp' }
  return $Category
}

try {
  Write-Host "============================================"
  Write-Host " SHIFT: scan games on THIS server -> JSON"
  Write-Host " Then import JSON in Shell panel"
  Write-Host "============================================"

  $scanDrives = @(Get-ScanDrives)
  Write-Host ("Drives: " + ($scanDrives -join ', '))
  Write-Host ""

  $apps = New-Object System.Collections.ArrayList
  $seenPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  $sort = 100

  foreach ($drv in $scanDrives) {
    $rootPath = $drv + '\'
    if (-not (Test-Path -LiteralPath $rootPath)) {
      Write-Warning ("Missing drive " + $drv)
      continue
    }

    Write-Host (">> " + $drv)
    foreach ($root in @(Get-GameRoots $drv)) {
      Write-Host ("   " + $root)
      foreach ($gameFolder in @(Get-CandidateFolders $root)) {
        try {
          $exe = Pick-BestExe $gameFolder
          if (-not $exe) { continue }

          $exePath = [string]$exe.FullName
          if (-not $seenPaths.Add($exePath)) { continue }

          $name = [string](Split-Path $gameFolder -Leaf)
          $name = ($name -replace '[_\.]+', ' ').Trim()
          if ($name.Length -lt 2) { $name = [string]$exe.BaseName }

          $item = [pscustomobject]@{
            name             = $name
            category         = [string](Guess-Category $exePath)
            exePath          = $exePath
            arguments        = $null
            workingDirectory = [string]$exe.DirectoryName
            iconPath         = $null
            sortOrder        = [int]$sort
            isActive         = $true
            minAge           = $null
          }
          [void]$apps.Add($item)
          $sort++
          Write-Host ("   + " + $name)
        } catch {
          Write-Warning ("Skip folder: " + $gameFolder + " :: " + $_.Exception.Message)
        }
      }
    }
  }

  $payload = [pscustomobject]@{
    schema      = "shiftclub.software-apps.v1"
    source      = [string]$env:COMPUTERNAME
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    apps        = @($apps.ToArray())
  }

  $dir = Split-Path -Parent $OutFile
  if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
  }

  # Build JSON manually enough to avoid ConvertTo-Json type quirks on PS 5.1
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('{')
  [void]$sb.AppendLine(('  "schema": ' + ($payload.schema | ConvertTo-Json -Compress) + ','))
  [void]$sb.AppendLine(('  "source": ' + ($payload.source | ConvertTo-Json -Compress) + ','))
  [void]$sb.AppendLine(('  "generatedAt": ' + ($payload.generatedAt | ConvertTo-Json -Compress) + ','))
  [void]$sb.AppendLine('  "apps": [')

  for ($i = 0; $i -lt $apps.Count; $i++) {
    $a = $apps[$i]
    $line = ($a | ConvertTo-Json -Compress -Depth 4)
    if ($i -lt ($apps.Count - 1)) { $line = $line + ',' }
    [void]$sb.AppendLine(('    ' + $line))
  }

  [void]$sb.AppendLine('  ]')
  [void]$sb.AppendLine('}')

  [System.IO.File]::WriteAllText($OutFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

  Write-Host ""
  Write-Host ("Found: " + $apps.Count)
  Write-Host ("JSON:  " + $OutFile)
  Write-Host ""
  Write-Host "Next:"
  Write-Host "  1) Copy JSON to cashier PC"
  Write-Host "  2) Panel -> Software -> Import JSON"
  exit 0
}
catch {
  Write-Host ""
  Write-Host ("ERROR: " + $_.Exception.GetType().FullName)
  Write-Host $_.Exception.Message
  Write-Host $_.ScriptStackTrace
  exit 1
}
