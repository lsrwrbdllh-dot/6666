#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$PackageRoot, [switch]$DisposableTest)
$ErrorActionPreference = 'Stop'
if (-not $DisposableTest) { throw 'Pass -DisposableTest; this check creates its own temporary LocalDB instance.' }
$PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('supermarket-lan-' + [Guid]::NewGuid().ToString('N'))
$instance = 'SupermarketLanCI_' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$ruleName = 'Supermarket-LAN-CI-' + [Guid]::NewGuid().ToString('N')
$process = $null
$sqlTool = $null

function Assert-Store { param([bool]$Condition,[string]$Message) if (-not $Condition) { throw $Message } }
function Read-StoreScalar {
    param([string]$Sql)
    $connection = New-Object System.Data.SqlClient.SqlConnection -ArgumentList $script:settings.ConnectionStrings.Supermarket
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        try { return $command.ExecuteScalar() } finally { $command.Dispose() }
    } finally { $connection.Dispose() }
}

try {
    foreach ($path in Get-ChildItem (Join-Path $PackageRoot 'scripts') -Filter '*.ps1') {
        $tokens=$null; $errors=$null
        [Management.Automation.Language.Parser]::ParseFile($path.FullName,[ref]$tokens,[ref]$errors) | Out-Null
        Assert-Store ($errors.Count -eq 0) ('PowerShell parse error in ' + $path.Name)
    }
    $tool = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
    if ($tool) { $sqlTool = $tool.Source }
    else {
        $sqlTool = Get-ChildItem (Join-Path $env:ProgramFiles 'Microsoft SQL Server') -Filter 'SqlLocalDB.exe' -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $sqlTool) { throw 'The Windows runner needs SQL Server LocalDB for this disposable integration check.' }
    & $sqlTool create $instance | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the temporary LocalDB instance.' }
    & $sqlTool start $instance | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start the temporary LocalDB instance.' }
    $setup = Join-Path $PackageRoot 'scripts\Setup-LAN.ps1'
    & $setup -SqlServer ('(localdb)\' + $instance) -DataDirectory $testRoot -PreferredAddress '192.168.8.25' -SkipFirewall -NonInteractive
    Assert-Store ($LASTEXITCODE -eq 0) 'Initial database setup failed.'
    $settingsPath = Join-Path $testRoot 'lan\server.json'
    $script:settings = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    Assert-Store ($settings.ApiKey.Length -eq 64) 'Setup did not generate a random 32-byte phone key.'
    $firstKey = $settings.ApiKey
    $windowsConnection = [IO.File]::ReadAllText((Join-Path $testRoot 'connection.json')) | ConvertFrom-Json
    Assert-Store ($windowsConnection -eq $settings.ConnectionStrings.Supermarket) 'Windows and phone do not share the same database.'
    $instructions = [IO.File]::ReadAllText((Join-Path $testRoot 'lan\Phone-Connection.txt'))
    Assert-Store ($instructions.Contains('http://192.168.8.25:5080') -and $instructions.Contains($firstKey)) 'Phone instructions omit the URL or key.'
    Assert-Store ((Read-StoreScalar 'SELECT COUNT(*) FROM dbo.Products') -eq 3) 'Fresh database seed is incorrect.'

    $startup = Join-Path $PackageRoot 'scripts\Start-StoreServer.ps1'
    $stdout = Join-Path $testRoot 'server-out.log'
    $stderr = Join-Path $testRoot 'server-error.log'
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$startup+'"'),'-DataDirectory',('"'+$testRoot+'"'),'-NoInstructionsWindow')
    $process = Start-Process -FilePath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $headers = @{ 'X-Api-Key'=$firstKey }
    $base = 'http://127.0.0.1:5080/api/'
    $ready = $false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw 'Packaged API launcher exited before readiness.' }
        try {
            $health = Invoke-RestMethod ($base+'health') -Headers $headers -TimeoutSec 2
            $ready = $true; break
        } catch { Start-Sleep -Seconds 1 }
    }
    Assert-Store $ready 'Packaged API could not connect to the newly configured database.'
    $unauthorized = $false
    try { Invoke-WebRequest ($base+'health') -Headers @{ 'X-Api-Key'='wrong' } -UseBasicParsing | Out-Null }
    catch { $unauthorized = [int]$_.Exception.Response.StatusCode -eq 401 }
    Assert-Store $unauthorized 'API accepted an invalid phone key.'
    $requestId = [Guid]::NewGuid().ToString()
    $body = @{ requestId=$requestId; kind='Capital'; amount=1000; note='LAN setup test' } | ConvertTo-Json
    $posted = Invoke-RestMethod ($base+'cash') -Method Post -Headers $headers -Body $body -ContentType 'application/json; charset=utf-8'
    $replayed = Invoke-RestMethod ($base+'cash') -Method Post -Headers $headers -Body $body -ContentType 'application/json; charset=utf-8'
    Assert-Store ($posted.id -eq $replayed.id) 'Phone retry duplicated a document.'
    Assert-Store ((Read-StoreScalar 'SELECT COUNT(*) FROM dbo.Documents') -eq 1) 'Phone operation was not visible in the desktop database.'
    $product = @{ barcode=[Guid]::NewGuid().ToString(); name='منتج من الهاتف'; salePrice=5; minimumStock=2; isActive=$true } | ConvertTo-Json
    Invoke-RestMethod ($base+'products') -Method Post -Headers $headers -Body $product -ContentType 'application/json; charset=utf-8' | Out-Null
    Assert-Store ((Read-StoreScalar "SELECT COUNT(*) FROM dbo.Products WHERE Name=N'منتج من الهاتف'") -eq 1) 'Arabic product did not reach the shared database.'

    & $setup -SqlServer ('(localdb)\' + $instance) -DataDirectory $testRoot -PreferredAddress '192.168.8.25' -SkipFirewall -NonInteractive
    Assert-Store ($LASTEXITCODE -eq 0) 'Repeating setup failed.'
    $after = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    Assert-Store ($after.ApiKey -eq $firstKey) 'Repeating setup changed the phone key.'
    Assert-Store ((Read-StoreScalar 'SELECT COUNT(*) FROM dbo.Documents') -eq 1) 'Repeating setup reset existing accounting data.'
    Assert-Store ((Read-StoreScalar 'SELECT COUNT(*) FROM dbo.Products') -eq 4) 'Repeating setup reseeded or removed products.'
    Invoke-RestMethod ($base+'health') -Headers @{ 'X-Api-Key'=$after.ApiKey } | Out-Null

    & (Join-Path $PackageRoot 'scripts\Enable-LanFirewall.ps1') -RuleName $ruleName -NoElevation -SkipNetworkCheck
    Assert-Store ($LASTEXITCODE -eq 0) 'Private LAN firewall setup failed.'
    $rule = Get-NetFirewallRule -Name $ruleName
    Assert-Store ($rule.Profile.ToString() -eq 'Private' -and $rule.Direction.ToString() -eq 'Inbound') 'Firewall is not limited to the private network.'
    $port = $rule | Get-NetFirewallPortFilter
    $address = $rule | Get-NetFirewallAddressFilter
    Assert-Store ($port.LocalPort -eq '5080' -and $port.Protocol -eq 'TCP' -and $address.RemoteAddress -contains 'LocalSubnet') 'Firewall does not use the intended LAN-only port and subnet.'
    Assert-Store (-not @(Get-ChildItem $PackageRoot -Recurse -File | Where-Object { $_.Name -in @('server.json','Phone-Connection.txt') }).Count) 'Package contains private runtime configuration.'
    Write-Host 'PASS: first setup, standalone API launch, authentication, shared SQL data, retries, repeated setup and private LAN firewall.'
} finally {
    if ($process) {
        Get-CimInstance Win32_Process -Filter ('ParentProcessId=' + $process.Id) -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq 'Supermarket.Api.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    if ($sqlTool) { & $sqlTool stop $instance -k | Out-Null; & $sqlTool delete $instance | Out-Null }
    if (Test-Path $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
