@echo off
setlocal
pushd "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
 echo Install .NET 10 SDK, maui-android, Android SDK 36 and JDK 21. Read mobile/README.md.
 popd
 exit /b 1
)
dotnet run --project tests/Madar.OfflineChecks -c Release
if errorlevel 1 goto failed
dotnet publish mobile/Supermarket.Android -c Release -r android-arm64 -p:AndroidPackageFormats=apk
if errorlevel 1 goto failed
echo APK: mobile/Supermarket.Android/bin/Release/net10.0-android/android-arm64/publish/*-Signed.apk
popd
exit /b 0
:failed
popd
exit /b 1
