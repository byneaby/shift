@echo off
REM SHIFT: fix Steam/Epic/Riot "registry not writable" before desktop apps start
if exist "D:\Apps\ShiftClub\LauncherFix\Fix-LauncherRegistry.ps1" (
  powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "D:\Apps\ShiftClub\LauncherFix\Fix-LauncherRegistry.ps1"
)
