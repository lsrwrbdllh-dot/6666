#requires -Version 5.1
param([string]$DataDirectory, [switch]$NoInstructionsWindow)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
. (Join-Path $PSScriptRoot 'Lan-Common.ps1')
try {
    $root = Get-StoreDataDirectory $DataDirectory
    $settingsPath = Join-Path (Join-Path $root 'lan') 'server.json'
    if (-not (Test-Path -LiteralPath $settingsPath)) { throw 'شغّل Setup-LAN.cmd أولاً لإنشاء القاعدة وإعداد الهاتف.' }
    $settings = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    if (-not $settings.ConnectionStrings.Supermarket -or $settings.ApiKey.Length -lt 32 -or $settings.ApiKey.Length -gt 256) { throw 'ملف إعداد الخادم غير مكتمل. أعد تشغيل Setup-LAN.cmd.' }
    $server = Join-Path (Split-Path $PSScriptRoot -Parent) 'server\Supermarket.Api.exe'
    if (-not (Test-Path -LiteralPath $server)) { throw 'هذه ملفات المصدر. نزّل حزمة supermarket-local-windows من GitHub Actions وفك ضغطها كاملة.' }
    $phoneFile = Write-StorePhoneInstructions $root $settings $settings.PreferredAddress
    $env:ConnectionStrings__Supermarket = [string]$settings.ConnectionStrings.Supermarket
    $env:ApiKey = [string]$settings.ApiKey
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://0.0.0.0:5080'
    if (-not $NoInstructionsWindow) { Start-Process -FilePath notepad.exe -ArgumentList ('"' + $phoneFile + '"') | Out-Null }
    Write-Host 'خادم الهاتف على شبكة المتجر. أبقِ هذه النافذة والكمبيوتر مشغّلين.'
    Write-Host 'انسخ العنوان والمفتاح من ملف بيانات الاتصال إلى تطبيق الهاتف.'
    & $server
    exit $LASTEXITCODE
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
