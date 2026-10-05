<#
  Общие настройки сборки и выкладки для одной установки.

  Раньше адрес сервера и пути к дискам были вписаны в сами скрипты, и собранный
  клиент одного клуба указывал на сервер другого. Теперь скрипты берут их отсюда,
  а файл deploy.config.json у каждой установки свой и в репозиторий не попадает.

  Порядок, в котором берётся значение:
    1. параметр, переданный скрипту;
    2. переменная окружения (SHIFTCLUB_SERVER_URL и прочие);
    3. scripts\deploy.config.json;
    4. безопасное значение по умолчанию, если оно вообще уместно.

  Файл подключается через точку:
    . (Join-Path $PSScriptRoot '_DeployConfig.ps1')
#>

function Get-ShiftClubDeployConfig {
  param(
    [string]$ScriptRoot = $PSScriptRoot
  )

  $config = @{
    ServerUrl   = ''
    ServerDir   = 'C:\ShiftClub\Server'
    ShellDirs   = @()
    DotnetRoot  = ''
    PackagesDir = ''
    ApiToken    = ''
  }

  $path = Join-Path $ScriptRoot 'deploy.config.json'
  if (Test-Path $path) {
    $json = Get-Content $path -Raw | ConvertFrom-Json
    foreach ($key in @($config.Keys)) {
      $value = $json.PSObject.Properties[$key]
      if ($null -ne $value -and $null -ne $value.Value -and "$($value.Value)" -ne '') {
        $config[$key] = $value.Value
      }
    }
  }

  # Переменные окружения перебивают файл: удобно для разового запуска и для CI.
  $fromEnv = @{
    ServerUrl   = $env:SHIFTCLUB_SERVER_URL
    ServerDir   = $env:SHIFTCLUB_SERVER_DIR
    DotnetRoot  = $env:SHIFTCLUB_DOTNET_ROOT
    PackagesDir = $env:SHIFTCLUB_PACKAGES_DIR
    ApiToken    = $env:SHIFTCLUB_API_TOKEN
  }
  foreach ($key in $fromEnv.Keys) {
    if ($fromEnv[$key]) { $config[$key] = $fromEnv[$key] }
  }
  if ($env:SHIFTCLUB_SHELL_DIRS) {
    $config.ShellDirs = $env:SHIFTCLUB_SHELL_DIRS -split ';' | Where-Object { $_ }
  }

  return $config
}

<#
  Адрес сервера попадает внутрь собранного клиента, поэтому угадывать его нельзя:
  с чужим адресом клиент молча не найдёт свой клуб.
#>
function Resolve-ShiftClubServerUrl {
  param(
    [string]$Provided,
    [hashtable]$Config
  )

  $url = if ($Provided) { $Provided } else { $Config.ServerUrl }
  if (-not $url) {
    throw @"
Не задан адрес сервера клуба.

Укажите одним из способов:
  -ServerUrl http://адрес-сервера:5080
  переменная окружения SHIFTCLUB_SERVER_URL
  поле serverUrl в scripts\deploy.config.json
    (возьмите за образец scripts\deploy.config.example.json)
"@
  }

  return $url.TrimEnd('/')
}

<#
  Подставляет в PATH тот dotnet, который указан в настройках. Если не указан —
  оставляем системный: на нормально настроенной машине он и так найдётся.
#>
function Use-ShiftClubDotnet {
  param([hashtable]$Config)

  if (-not $Config.DotnetRoot) { return }
  if (-not (Test-Path (Join-Path $Config.DotnetRoot 'dotnet.exe'))) {
    Write-Warning "В настройках указан dotnetRoot '$($Config.DotnetRoot)', но dotnet.exe там нет — берём системный"
    return
  }

  $env:DOTNET_ROOT = $Config.DotnetRoot
  $env:PATH = "$($Config.DotnetRoot);$env:PATH"
}
