#requires -Version 5.1
param(
    [string]$RuleName = 'SupermarketAccounting-LAN',
    [switch]$NoElevation,
    [switch]$SkipNetworkCheck
)
$ErrorActionPreference = 'Stop'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal -ArgumentList $identity
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        if ($NoElevation) { throw 'يلزم حساب مدير لإعداد جدار الحماية.' }
        $process = Start-Process -FilePath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"')) -Verb RunAs -Wait -PassThru
        exit $process.ExitCode
    }
    if (-not $SkipNetworkCheck -and -not @(Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Private' -and $_.IPv4Connectivity -ne 'Disconnected' }).Count) {
        throw 'اجعل شبكة Wi-Fi الموثوقة الخاصة بالمتجر Private من إعدادات Windows، ثم أعد تشغيل الإعداد.'
    }
    $rule = Get-NetFirewallRule -Name $RuleName -ErrorAction SilentlyContinue
    if ($rule) {
        Set-NetFirewallRule -Name $RuleName -Enabled True -Direction Inbound -Action Allow -Profile Private -Protocol TCP -LocalPort 5080 -RemoteAddress LocalSubnet | Out-Null
    } else {
        New-NetFirewallRule -Name $RuleName -DisplayName 'Supermarket phone on local Wi-Fi' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5080 -RemoteAddress LocalSubnet -Profile Private | Out-Null
    }
    Write-Host 'تم السماح باتصال الهاتف على المنفذ 5080 داخل الشبكة المحلية الخاصة.'
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
