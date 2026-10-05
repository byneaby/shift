# Restores the SHIFT Club database snapshot into a local PostgreSQL (dev machine).
#
#   .\restore.ps1                         # DB "shiftclub", owner role "shiftclub", connects as postgres
#   .\restore.ps1 -DbName shiftclub_dev -PgBin "C:\Program Files\PostgreSQL\18\bin"
#
# Requires PostgreSQL 18 client tools (pg_restore/psql). You will be asked for the postgres password.
param(
  [string]$DbName = "shiftclub",
  [string]$Owner = "shiftclub",
  [string]$OwnerPassword = "shiftclub_dev",
  [string]$SuperUser = "postgres",
  [string]$PgHost = "127.0.0.1",
  [int]$Port = 5432,
  [string]$PgBin = "C:\Program Files\PostgreSQL\18\bin"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$psql = Join-Path $PgBin "psql.exe"
$pgRestore = Join-Path $PgBin "pg_restore.exe"
foreach ($exe in @($psql, $pgRestore)) { if (-not (Test-Path $exe)) { throw "Not found: $exe (set -PgBin)" } }

if (-not $env:PGPASSWORD) {
  $sec = Read-Host "Password for $SuperUser" -AsSecureString
  $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

function Invoke-Psql([string]$db, [string]$sql) {
  & $psql -X -v ON_ERROR_STOP=1 -h $PgHost -p $Port -U $SuperUser -d $db -c $sql
  if ($LASTEXITCODE) { throw "psql failed: $sql" }
}

Write-Host "== Role $Owner"
Invoke-Psql "postgres" "DO `$`$BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='$Owner') THEN CREATE ROLE $Owner LOGIN PASSWORD '$OwnerPassword'; END IF; END`$`$;"

Write-Host "== Database $DbName (recreated)"
Invoke-Psql "postgres" "DROP DATABASE IF EXISTS $DbName WITH (FORCE);"
Invoke-Psql "postgres" "CREATE DATABASE $DbName OWNER $Owner;"

Write-Host "== Restore data"
& $pgRestore -h $PgHost -p $Port -U $SuperUser -d $DbName --no-owner --role=$Owner --exit-on-error (Join-Path $here "shiftclub_full.dump")
if ($LASTEXITCODE) { throw "pg_restore failed" }

Write-Host "== app_settings (without secrets)"
& $psql -X -v ON_ERROR_STOP=1 -h $PgHost -p $Port -U $SuperUser -d $DbName -f (Join-Path $here "app_settings_safe.sql")
if ($LASTEXITCODE) { throw "app_settings import failed" }

Invoke-Psql $DbName "GRANT ALL ON ALL TABLES IN SCHEMA public TO $Owner; GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO $Owner;"

Write-Host ""
Write-Host "Done. Connection string for secrets.env:"
Write-Host "ConnectionStrings__Default=Host=$PgHost;Port=$Port;Database=$DbName;Username=$Owner;Password=$OwnerPassword"
