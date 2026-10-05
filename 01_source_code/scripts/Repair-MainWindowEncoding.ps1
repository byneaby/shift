$ErrorActionPreference = 'Stop'
$shellDir = 'D:\ShiftClub\SHIFT PROJECT\src\ShiftClub.Client\ShiftClub.Client.Shell'
$qbakPath = Join-Path $shellDir 'MainWindow.xaml.cs.questions-bak'
$repPath = Join-Path $shellDir 'MainWindow.xaml.cs.repaired'
$outPath = Join-Path $shellDir 'MainWindow.xaml.cs'

$qbak = [IO.File]::ReadAllText($qbakPath)
$rep = [IO.File]::ReadAllText($repPath)

function Test-Broken([string]$s) {
  if ([string]::IsNullOrEmpty($s)) { return $false }
  if ($s -match '\?\?\?') { return $true }
  $q = ([regex]::Matches($s, '\?')).Count
  return ($q -ge 2 -and ($q / [double][Math]::Max(1, $s.Length)) -ge 0.25)
}

function Escape-Inner([string]$s) {
  return $s.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
}

# Build map: LHS assignment key -> good string from repaired
# e.g. "HomeHello.Text" -> "ПРИВЕТ, {first...}!"
$litRx = New-Object System.Text.RegularExpressions.Regex '\$?"((?:\\.|[^"\\])*)"'
$assignRx = New-Object System.Text.RegularExpressions.Regex '(?m)^(?<indent>\s*)(?<lhs>[A-Za-z_][\w\.\?]*(?:\?\?)?[A-Za-z0-9_\.]*)\s*=\s*(?<prefix>\$?")(?<body>(?:\\.|[^"\\])*)"'

$repMap = @{} # lhs -> list of bodies (in order)
foreach ($m in $assignRx.Matches($rep)) {
  $lhs = $m.Groups['lhs'].Value
  $body = $m.Groups['body'].Value
  if ($body -notmatch '[\u0400-\u04FF]' -and -not (Test-Broken $body)) { continue }
  if (-not $repMap.ContainsKey($lhs)) { $repMap[$lhs] = New-Object System.Collections.Generic.List[string] }
  $repMap[$lhs].Add($body)
}
Write-Host "rep LHS keys with text: $($repMap.Count)"

# Also map by ShowInfoAsync / ConfirmAsync first args etc via function call patterns
# Walk qbak assignments and replace broken bodies
$qCounters = @{}
$repls = New-Object System.Collections.Generic.List[object]
$replaced = 0
$missed = 0

foreach ($m in $assignRx.Matches($qbak)) {
  $body = $m.Groups['body'].Value
  if (-not (Test-Broken $body)) { continue }
  $lhs = $m.Groups['lhs'].Value
  $prefix = $m.Groups['prefix'].Value
  $good = $null
  if ($repMap.ContainsKey($lhs)) {
    $list = $repMap[$lhs]
    $idx = 0
    if ($qCounters.ContainsKey($lhs)) { $idx = $qCounters[$lhs] }
    if ($idx -lt $list.Count) {
      $good = $list[$idx]
      $qCounters[$lhs] = $idx + 1
    }
  }
  if ($null -eq $good) {
    $missed++
    Write-Host "MISS lhs=$lhs bodyLen=$($body.Length)"
    continue
  }
  # For qbak that had extra " ??" emoji suffix, prefer repaired as-is
  $newFull = $prefix + (Escape-Inner $good) + '"'
  # m.Value may include only from lhs... need full match index of the string literal part
  # Reconstruct: find the string literal within this match
  $fullAssign = $m.Value
  $strStartInMatch = $fullAssign.IndexOf($prefix)
  $absStart = $m.Index + $strStartInMatch
  $absLen = $prefix.Length + $body.Length + 1 # closing quote
  $repls.Add([pscustomobject]@{ Index = $absStart; Length = $absLen; New = $newFull })
  $replaced++
}
Write-Host "assignment replaced=$replaced missed=$missed"

# Also fix non-assignment string lits (ternaries, args) that are broken — match by nearby unique ASCII
# Extract all broken lits from qbak with preceding 40 chars of code as key
function Get-ContextKey([string]$text, [int]$index) {
  $start = [Math]::Max(0, $index - 60)
  $ctx = $text.Substring($start, $index - $start)
  # keep only ASCII identifiers
  return [regex]::Replace($ctx, '[^\x20-\x7E]', '')
}

$repLits = @()
foreach ($m in $litRx.Matches($rep)) {
  if ($m.Groups[1].Value -match '[\u0400-\u04FF]') {
    $repLits += [pscustomobject]@{
      Body = $m.Groups[1].Value
      IsInterp = $m.Value.StartsWith('$"')
      Ctx = Get-ContextKey $rep $m.Index
    }
  }
}

foreach ($m in $litRx.Matches($qbak)) {
  $body = $m.Groups[1].Value
  if (-not (Test-Broken $body)) { continue }
  # skip if already covered by assignment repls
  $covered = $false
  foreach ($r in $repls) {
    if ($m.Index -ge $r.Index -and $m.Index -lt ($r.Index + $r.Length)) { $covered = $true; break }
  }
  if ($covered) { continue }

  $ctx = Get-ContextKey $qbak $m.Index
  $bp = ([regex]::Matches($body, '\{')).Count
  $best = $null
  $bestScore = 1e9
  foreach ($g in $repLits) {
    if ($g.IsInterp -ne $m.Value.StartsWith('$"')) { continue }
    $gp = ([regex]::Matches($g.Body, '\{')).Count
    if ($gp -ne $bp) { continue }
    # context similarity: longest common ASCII suffix
    $score = [Math]::Abs($body.Length - $g.Body.Length)
    $a = $ctx; $b = $g.Ctx
    $common = 0
    $mi = [Math]::Min($a.Length, $b.Length)
    for ($i = 1; $i -le $mi; $i++) {
      if ($a.Substring($a.Length - $i) -eq $b.Substring($b.Length - $i)) { $common = $i } else { break }
    }
    $score -= $common * 0.5
    if ($common -lt 8) { $score += 20 }
    if ($score -lt $bestScore) { $bestScore = $score; $best = $g }
  }
  if ($null -ne $best -and $bestScore -lt 25) {
    $prefix = if ($m.Value.StartsWith('$"')) { '$"' } else { '"' }
    $newFull = $prefix + (Escape-Inner $best.Body) + '"'
    $repls.Add([pscustomobject]@{ Index = $m.Index; Length = $m.Length; New = $newFull })
    $replaced++
  }
  else {
    $missed++
    Write-Host ("MISS ctx lit len={0} score={1} ctx=...{2}" -f $body.Length, $bestScore, $ctx.Substring([Math]::Max(0, $ctx.Length - 40)))
  }
}

Write-Host "total repls=$($repls.Count)"
$sb = New-Object System.Text.StringBuilder $qbak
foreach ($r in ($repls | Sort-Object Index -Descending)) {
  [void]$sb.Remove($r.Index, $r.Length)
  [void]$sb.Insert($r.Index, $r.New)
}
$fixed = $sb.ToString()
$fixed = $fixed.Replace('http://192.168.1.200:5080', 'http://192.168.1.250:5080')

# Fix remaining ??? by scanning
$left = @()
foreach ($m in $litRx.Matches($fixed)) {
  if ($m.Groups[1].Value -match '\?\?\?') { $left += $m }
}
Write-Host "left ???=$($left.Count) cyr=$(([regex]::Matches($fixed,'[\u0400-\u04FF]')).Count)"

[IO.File]::WriteAllText($outPath, $fixed, (New-Object System.Text.UTF8Encoding $true))

# Verify critical
foreach ($name in @('HomeHello.Text', 'OnlineText.Text', 'BalanceLabel.Text')) {
  $line = ($fixed -split "`r?`n" | Where-Object { $_ -match [regex]::Escape($name) + '\s*=' } | Select-Object -First 1)
  $mm = [regex]::Match($line, '\$?"([^"]*)"')
  $s = $mm.Groups[1].Value
  $hex = (($s.ToCharArray() | Select-Object -First 12 | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ')
  Write-Host "$name => $hex"
}
