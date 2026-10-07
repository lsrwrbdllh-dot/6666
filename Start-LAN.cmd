@echo off
chcp 65001 >nul
setlocal
"%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-StoreServer.ps1"
set "store_server_result=%ERRORLEVEL%"
if not "%store_server_result%"=="0" pause
exit /b %store_server_result%
