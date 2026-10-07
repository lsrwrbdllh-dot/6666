@echo off
chcp 65001 >nul
setlocal
"%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Setup-LAN.ps1"
set "store_setup_result=%ERRORLEVEL%"
pause
exit /b %store_setup_result%
