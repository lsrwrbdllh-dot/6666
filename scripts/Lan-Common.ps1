function Get-StoreDataDirectory {
    param([string]$DataDirectory)
    if ($DataDirectory) { return [IO.Path]::GetFullPath($DataDirectory) }
    return Join-Path $env:LOCALAPPDATA 'SupermarketAccounting'
}

function Protect-StoreDirectory {
    param([string]$Path)
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetOwner($owner)
    foreach ($sid in @($owner, [Security.Principal.SecurityIdentifier]'S-1-5-18', [Security.Principal.SecurityIdentifier]'S-1-5-32-544')) {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule -ArgumentList @($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Write-StoreUtf8File {
    param([string]$Path, [string]$Content)
    $temporary = $Path + '.tmp'
    [IO.File]::WriteAllText($temporary, $Content, (New-Object System.Text.UTF8Encoding -ArgumentList $false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Get-StoreLanAddresses {
    $candidates = @()
    foreach ($adapter in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($adapter.OperationalStatus -ne 'Up' -or $adapter.NetworkInterfaceType -in @('Loopback','Tunnel')) { continue }
        $properties = $adapter.GetIPProperties()
        if (-not @($properties.GatewayAddresses | Where-Object { $_.Address.AddressFamily -eq 'InterNetwork' -and $_.Address.ToString() -ne '0.0.0.0' }).Count) { continue }
        foreach ($address in $properties.UnicastAddresses) {
            if ($address.Address.AddressFamily -ne 'InterNetwork') { continue }
            $bytes = $address.Address.GetAddressBytes()
            if ($bytes[0] -eq 10 -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168) -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31)) {
                $candidates += [pscustomobject]@{ Address=$address.Address.ToString(); Priority=([int]($adapter.NetworkInterfaceType -ne 'Wireless80211')) }
            }
        }
    }
    return @($candidates | Sort-Object Priority,Address | Select-Object -ExpandProperty Address -Unique)
}

function Write-StorePhoneInstructions {
    param([string]$DataDirectory, [object]$Settings, [string]$PreferredAddress)
    $addresses = @(Get-StoreLanAddresses)
    if ($PreferredAddress) {
        $parsed = $null
        if (-not [Net.IPAddress]::TryParse($PreferredAddress, [ref]$parsed) -or $parsed.AddressFamily -ne 'InterNetwork') { throw 'عنوان IPv4 غير صحيح.' }
        $addresses = @($parsed.ToString())
    }
    $url = if ($addresses.Count) { 'http://' + $addresses[0] + ':5080' } else { 'لم يظهر عنوان شبكة محلية. صِل الكمبيوتر بشبكة المتجر ثم شغّل Start-LAN.cmd مجدداً.' }
    $text = @"
ربط الهاتف بالكمبيوتر — محاسبة السوبر ماركت

أوصل الهاتف والكمبيوتر بنفس شبكة Wi-Fi، وافتح تطبيق الهاتف ثم إعداد الاتصال.
انسخ العنوان التالي في الخانة الأولى:
$url

انسخ مفتاح الاتصال التالي في الخانة الثانية:
$($Settings.ApiKey)

اضغط حفظ واختبار الاتصال.
أبقِ الكمبيوتر ونافذة خادم الهاتف مشغّلين أثناء استخدام الهاتف.
بياناتك على قاعدة SQL Server في الكمبيوتر؛ برنامج الكمبيوتر والهاتف يستخدمان نفس القاعدة.
إذا تغيّر عنوان الكمبيوتر، شغّل Start-LAN.cmd مجدداً وانسخ العنوان الجديد.
لا ترسل هذا الملف إلى أشخاص غير مخولين باستخدام النظام.
"@
    $path = Join-Path (Join-Path $DataDirectory 'lan') 'Phone-Connection.txt'
    Write-StoreUtf8File $path $text
    return $path
}
