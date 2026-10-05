@echo off
:: Bypass ExecutionPolicy — run the NVIDIA CCBoot image fix as Administrator
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-NvidiaContainerFix.ps1" %*
echo.
pause
