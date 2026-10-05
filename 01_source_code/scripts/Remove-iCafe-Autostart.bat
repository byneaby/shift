@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

:: Убрать автозапуск iCafeMenu на клиенте / SuperClient (от Администратора).
:: Главная причина запуска: Winlogon Shell/Userinit и CCBootClient (diskless),
:: а не только папка Startup. CCBoot-сервер не трогает.

net session >nul 2>&1
if errorlevel 1 (
  echo [!] Нужны права администратора.
  echo     ПКМ -^> "Запуск от имени администратора"
  pause
  exit /b 1
)

echo ========================================
echo  SHIFT: отключение iCafeMenu (полный)
echo ========================================
echo.

echo [1/6] Стоп процессов iCafe / Overwolf...
for %%P in (
  iCafeMenu.exe iCafeMenuBt.exe iCafeClient.exe
  Overwolf.exe OverwolfHelper.exe OverwolfHelper64.exe
  OverwolfBrowser.exe OverwolfLauncher.exe OverwolfUpdater.exe OverwolfStore.exe
) do taskkill /F /IM %%P /T >nul 2>&1

echo [2/6] Winlogon: Shell=explorer, Userinit=userinit.exe
:: Именно здесь iCafe ставит себя "до рабочего стола"
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d "explorer.exe" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Userinit /t REG_SZ /d "C:\Windows\system32\userinit.exe," /f >nul
echo   Shell / Userinit сброшены.

echo [3/6] Чистка Run (HKLM/HKCU)...
for %%K in (
  "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
  "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
  "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
  "HKCU\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
  "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
  "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
) do (
  for %%V in (iCafeMenu iCafe iCafeCloud iCafeCloudServer iCafeClient icafemenu Overwolf OverwolfLauncher) do (
    reg delete %%K /v %%V /f >nul 2>&1
  )
)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$roots=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run','HKCU:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run');" ^
  "foreach($r in $roots){ if(-not (Test-Path $r)){continue}; $p=Get-ItemProperty $r; $p.PSObject.Properties | Where-Object { $_.MemberType -eq 'NoteProperty' -and $_.Name -notmatch '^PS' -and ($_.Value -match 'iCafeMenu|icafemenu|iCafeCloud|Overwolf') } | ForEach-Object { Remove-ItemProperty -Path $r -Name $_.Name -Force -ErrorAction SilentlyContinue } }"

echo [4/6] Ярлыки Startup...
for %%D in (
  "%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp"
  "%AppData%\Microsoft\Windows\Start Menu\Programs\Startup"
) do if exist "%%~D" del /F /Q "%%~D\*iCafe*" "%%~D\*icafe*" "%%~D\*Overwolf*" >nul 2>&1

echo [5/6] Задачи планировщика iCafe...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match 'iCafe|icafe|Overwolf' -or $_.TaskPath -match 'iCafe|icafe|Overwolf' } | ForEach-Object { Unregister-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath -Confirm:$false -ErrorAction SilentlyContinue }"

echo [6/6] Службы iCafe на клиенте (если есть)...
for %%S in (iCafeCloudServer iCafeMenu iCafeClient) do (
  sc.exe query %%S >nul 2>&1
  if not errorlevel 1 (
    sc.exe stop %%S >nul 2>&1
    sc.exe config %%S start= disabled >nul 2>&1
  )
)

echo.
echo Готово на этом ПК/образе.
echo.
echo ОБЯЗАТЕЛЬНО на сервере в iCafeCloud CP:
echo   Settings -^> Client Settings
echo   - Run iCafeMenu on diskless = No / Disabled
echo   - Run iCafeMenu before desktop = No / Disabled
echo Иначе CCBootClient снова поднимет меню с game disk.
echo.
echo Потом: перезагрузка -^> сохранить образ SuperClient.
echo CCBoot (загрузка диска) не отключается этим скриптом.
echo.
pause
endlocal
