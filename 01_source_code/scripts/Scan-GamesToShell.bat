@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo  SHIFT Club - scan games on THIS server
echo  Output: shell-apps-import.json
echo  Copy JSON to cashier PC and import in panel
echo ============================================
echo.

where powershell >nul 2>&1
if errorlevel 1 (
  echo ERROR: PowerShell not found.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scan-GamesToShell.ps1" -OutFile "%~dp0shell-apps-import.json"
set ERR=%ERRORLEVEL%

echo.
if %ERR% neq 0 (
  echo FAILED. Exit code %ERR%
) else (
  echo OK. File: %~dp0shell-apps-import.json
)
echo.
pause
endlocal
exit /b %ERR%
