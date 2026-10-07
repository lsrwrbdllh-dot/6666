@echo off
setlocal
if not exist "%~dp0windows\SupermarketAccounting.exe" (
 echo Download and extract the supermarket-local-windows package from GitHub Actions first.
 pause
 exit /b 1
)
start "" "%~dp0windows\SupermarketAccounting.exe"
