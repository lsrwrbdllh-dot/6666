@echo off
chcp 65001 >nul
setlocal
pushd "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
 echo .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
 echo Then run this file again.
 pause
 popd
 exit /b 1
)
dotnet build "SupermarketAccounting.sln" -c Release
if errorlevel 1 (
 echo Build failed. Copy the error above for support.
 pause
 popd
 exit /b 1
)
echo Before connecting, run database/Setup.sql once using SSMS.
dotnet run --project "src/SupermarketAccounting/SupermarketAccounting.csproj" -c Release --no-build
if errorlevel 1 pause
popd
endlocal
