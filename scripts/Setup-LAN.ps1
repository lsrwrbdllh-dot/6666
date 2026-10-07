#requires -Version 5.1
param(
    [string]$SqlServer,
    [string]$DataDirectory,
    [string]$PreferredAddress,
    [switch]$SkipFirewall,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
. (Join-Path $PSScriptRoot 'Lan-Common.ps1')

try {
    if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'هذا الإعداد مخصص لويندوز.' }
    if (-not $SqlServer) {
        if ($NonInteractive) { $SqlServer = '.\SQLEXPRESS' }
        else {
            Write-Host 'اسم SQL Server الافتراضي هو .\SQLEXPRESS. اضغط Enter إذا ثبّت SQL Server Express بالإعداد الأساسي.'
            $SqlServer = Read-Host 'SQL Server'
            if (-not $SqlServer.Trim()) { $SqlServer = '.\SQLEXPRESS' }
        }
    }
    $SqlServer = $SqlServer.Trim()
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder.DataSource = $SqlServer
    $builder.InitialCatalog = 'master'
    $builder.IntegratedSecurity = $true
    $builder.Encrypt = $true
    $builder.TrustServerCertificate = $true
    $builder.ConnectTimeout = 8
    $connection = New-Object System.Data.SqlClient.SqlConnection -ArgumentList $builder.ConnectionString
    try {
        Write-Host 'جارٍ التحقق من SQL Server...'
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "SELECT DB_ID(N'SupermarketAccounting')"
        $exists = $command.ExecuteScalar()
        $command.Dispose()
        if ($null -eq $exists -or $exists -is [DBNull]) {
            $schema = Join-Path (Split-Path $PSScriptRoot -Parent) 'database\Setup.sql'
            $script = [IO.File]::ReadAllText($schema, [Text.Encoding]::UTF8)
            foreach ($batch in [regex]::Split($script, '(?im)^\s*GO\s*$')) {
                if (-not $batch.Trim()) { continue }
                $command = $connection.CreateCommand()
                $command.CommandTimeout = 120
                $command.CommandText = $batch
                try { $command.ExecuteNonQuery() | Out-Null } finally { $command.Dispose() }
            }
            Write-Host 'تم إنشاء قاعدة SupermarketAccounting.'
        } else { Write-Host 'القاعدة موجودة؛ سيتم استخدام بياناتها الحالية.' }
        $connection.ChangeDatabase('SupermarketAccounting')
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT CASE WHEN OBJECT_ID('dbo.Products','U') IS NOT NULL
 AND OBJECT_ID('dbo.Parties','U') IS NOT NULL AND OBJECT_ID('dbo.Accounts','U') IS NOT NULL
 AND OBJECT_ID('dbo.Documents','U') IS NOT NULL AND OBJECT_ID('dbo.DocumentLines','U') IS NOT NULL
 AND OBJECT_ID('dbo.Journal','U') IS NOT NULL AND OBJECT_ID('dbo.PostDocument','P') IS NOT NULL
 AND OBJECT_ID('dbo.PostSettlement','P') IS NOT NULL AND TYPE_ID('dbo.InvoiceItems') IS NOT NULL
 AND COL_LENGTH('dbo.Products','StockValue') IS NOT NULL THEN 1 ELSE 0 END
"@
        try { $valid = [int]$command.ExecuteScalar() } finally { $command.Dispose() }
        if ($valid -ne 1) { throw 'القاعدة موجودة لكن تركيبها غير متوافق أو غير مكتمل. لم تتم إعادة إنشائها أو حذف بياناتها. راجع ملف Setup.sql مع مسؤول القاعدة.' }
    } finally { $connection.Dispose() }

    $builder.InitialCatalog = 'SupermarketAccounting'
    $builder.ConnectTimeout = 15
    $root = Get-StoreDataDirectory $DataDirectory
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $lanDirectory = Join-Path $root 'lan'
    Protect-StoreDirectory $lanDirectory
    $settingsPath = Join-Path $lanDirectory 'server.json'
    if (Test-Path -LiteralPath $settingsPath) {
        $oldSettings = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
        $key = [string]$oldSettings.ApiKey
        if ($key.Length -lt 32 -or $key.Length -gt 256) { throw 'مفتاح الاتصال المحفوظ غير صحيح. راجع server.json قبل تغيير إعداد الهاتف.' }
    } else {
        $bytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) } finally { $random.Dispose() }
        $key = [BitConverter]::ToString($bytes).Replace('-','')
    }
    $settings = [pscustomobject]@{ ApiKey=$key; ConnectionStrings=@{ Supermarket=$builder.ConnectionString }; PreferredAddress=$PreferredAddress }
    Write-StoreUtf8File $settingsPath ($settings | ConvertTo-Json -Depth 5)
    $windowsSettings = Join-Path $root 'connection.json'
    if (Test-Path -LiteralPath $windowsSettings) {
        Copy-Item -LiteralPath $windowsSettings -Destination ($windowsSettings + '.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfffffff') + '.bak')
    }
    Write-StoreUtf8File $windowsSettings (ConvertTo-Json -InputObject $builder.ConnectionString)
    $phoneFile = Write-StorePhoneInstructions $root $settings $PreferredAddress
    if (-not $SkipFirewall) {
        & (Join-Path $PSScriptRoot 'Enable-LanFirewall.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'تعذر إعداد منفذ الهاتف. غيّر شبكة المتجر إلى Private ثم أعد تشغيل Setup-LAN.cmd ووافق على طلب صلاحية المدير.' }
    }
    Write-Host 'الإعداد جاهز. شغّل Start-Windows.cmd للكمبيوتر وStart-LAN.cmd لخادم الهاتف.'
    Write-Host ('بيانات الاتصال محفوظة في: ' + $phoneFile)
    exit 0
} catch {
    Write-Host ('تعذر الإعداد: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'إذا لم يثبت SQL Server، ثبّت SQL Server Express أولاً ثم أعد المحاولة. يلزم أن يملك حساب Windows صلاحيات إنشاء القاعدة.'
    exit 1
}
