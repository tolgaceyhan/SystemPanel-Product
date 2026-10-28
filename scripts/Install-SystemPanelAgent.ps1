<#
.SYNOPSIS
    SystemPanel Agent'ı Windows servisi olarak kurar / günceller.

.DESCRIPTION
    - Yayın klasörünü (dotnet publish çıktısı) -InstallPath altına kopyalar.
    - %ProgramData%\SystemPanel\Agent\agentsettings.json dosyasına ServerUrl, AgentId ve DPAPI
      (LocalMachine) ile korunan agent anahtarını yazar. Anahtar ekrana/komut geçmişine yazılmaz.
    - Ayar dosyasının erişimini Administrators, SYSTEM ve servis hesabıyla sınırlar.
    - Servisi düşük yetkili sanal hesapla (NT SERVICE\SystemPanelAgent) kurar, CPU sayaçları için
      "Performance Monitor Users" grubuna ekler ve hata durumunda yeniden başlatmayı ayarlar.
    Yönetici olarak çalıştırılmalıdır. Ayrıntılar: docs/AGENT.md

.EXAMPLE
    .\Install-SystemPanelAgent.ps1 -ServerUrl https://systempanel.kurum.local -AgentId 3f0c... -SourcePath .\publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^https?://')][string]$ServerUrl,
    [Parameter(Mandatory = $true)][guid]$AgentId,
    [string]$SourcePath = (Join-Path $PSScriptRoot 'publish'),
    [string]$InstallPath = (Join-Path $env:ProgramFiles 'SystemPanel\Agent'),
    [switch]$AllowInsecureHttp
)

$ErrorActionPreference = 'Stop'
$serviceName = 'SystemPanelAgent'
$serviceAccount = "NT SERVICE\$serviceName"
$exe = Join-Path $InstallPath 'SystemPanel.Agent.exe'
$configDir = Join-Path $env:ProgramData 'SystemPanel\Agent'
$configFile = Join-Path $configDir 'agentsettings.json'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bu betik yönetici olarak çalıştırılmalıdır.'
}
if ($ServerUrl -like 'http://*' -and -not $AllowInsecureHttp) {
    throw 'ServerUrl HTTPS olmalıdır. Geçici olarak HTTP gerekiyorsa -AllowInsecureHttp kullanın (anahtar ağda açık metin taşınır).'
}
if (-not (Test-Path (Join-Path $SourcePath 'SystemPanel.Agent.exe'))) {
    throw "Yayın klasöründe SystemPanel.Agent.exe bulunamadı: $SourcePath"
}

# 1) Anahtarı güvenli al ve DPAPI ile koru
Add-Type -AssemblyName System.Security
$secure = Read-Host -Prompt 'Agent anahtarı (SystemPanel > Agent''lar ekranında bir kez gösterilen değer)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    if ($plain -notlike 'spa_*') { throw 'Anahtar biçimi geçersiz (spa_ ile başlamalı).' }
    $entropy = [Text.Encoding]::UTF8.GetBytes('SystemPanel.Agent.Key.v1')
    $protected = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($plain), $entropy, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $protectedKey = 'dpapi:' + [Convert]::ToBase64String($protected)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Variable plain -ErrorAction SilentlyContinue
}

# 2) Servisi durdur (güncelleme) ve dosyaları kopyala
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallPath -Recurse -Force

# 3) Makineye özel ayarlar
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$settings = [ordered]@{
    Agent = [ordered]@{
        ServerUrl         = $ServerUrl.TrimEnd('/')
        AgentId           = $AgentId.ToString()
        AgentKey          = $protectedKey
        AllowInsecureHttp = [bool]$AllowInsecureHttp
    }
}
$settings | ConvertTo-Json -Depth 4 | Set-Content -Path $configFile -Encoding UTF8

# 4) Servis
if (-not $existing) {
    New-Service -Name $serviceName -BinaryPathName "`"$exe`"" -DisplayName 'SystemPanel Agent' `
        -Description 'SystemPanel sunucu metrik ajanı' -StartupType Automatic | Out-Null
}
& sc.exe config $serviceName obj= $serviceAccount | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

# 5) Yetkiler: ayar dosyası ve tampon klasörü yalnızca yöneticiler, SYSTEM ve servis hesabı
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($id in @('BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($serviceAccount, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -Path $configDir -AclObject $acl

try { Add-LocalGroupMember -Group 'Performance Monitor Users' -Member $serviceAccount -ErrorAction Stop }
catch { if ($_.Exception.Message -notmatch 'already a member|zaten') { Write-Warning "Performance Monitor Users üyeliği eklenemedi: $($_.Exception.Message)" } }

# 6) Olay günlüğü kaynağı (ilk kurulum)
if (-not [Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}

Start-Service -Name $serviceName
Write-Host "SystemPanel Agent kuruldu ve başlatıldı. Durum: SystemPanel > Agent'lar ekranı, Olay Görüntüleyicisi > Application > $serviceName"
