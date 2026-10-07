@echo off
setlocal
pushd "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
 echo Install .NET 10 SDK and the Android workload first. Read mobile/README.md.
 pause
 popd
 exit /b 1
)
dotnet publish mobile/Supermarket.Android -c Debug -p:AndroidPackageFormats=apk
if errorlevel 1 (
 echo Build failed. See the error above.
) else (
 echo APK files are under mobile/Supermarket.Android/bin/Debug.
)
pause
popd
endlocal
