<#
.SYNOPSIS
    SystemPanel kurulumundan ÖNCE ortamın hazır olup olmadığını kontrol eder (salt okunur).

.DESCRIPTION
    Hiçbir şeyi değiştirmez, kurmaz veya başlatmaz. Her kontrol için OK / UYARI / HATA / ATLANDI üretir.
    Rol seçimi:
      Web        IIS + ASP.NET Core Module, sertifika, SQL ve LDAPS erişimi, RSAT modülleri, disk
      Agent      Panel adresine HTTPS erişimi ve sertifika güveni
      Dashboard  WebView2 Runtime ve panel erişimi

    Rapor varsayılan olarak sunucu, domain ve sertifika adlarını GİZLER; böylece çıktı destek ekibine
    gönderilebilir. Adları görmek için -IncludeNames kullanın (bu çıktıyı kurum dışına göndermeyin).
    Ayrıntılar: docs/FIELD-VALIDATION.md

.EXAMPLE
    .\Test-SystemPanelPrerequisites.ps1 -Role Web -HostName panel.kurum.local -CertificateThumbprint <parmak-izi> -SqlServer sql01 -LdapHost dc01.kurum.local -OutputPath .\onkosul.json

.EXAMPLE
    .\Test-SystemPanelPrerequisites.ps1 -Role Agent -HostName panel.kurum.local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Web', 'Agent', 'Dashboard')][string[]]$Role,
    [ValidatePattern('^[A-Za-z0-9.-]+$')][string]$HostName,
    [ValidatePattern('^[A-Fa-f0-9 ]+$')][string]$CertificateThumbprint,
    [ValidatePattern('^[A-Za-z0-9._\\,-]+$')][string]$SqlServer,
    [ValidateRange(1, 65535)][int]$SqlPort = 1433,
    [ValidatePattern('^[A-Za-z0-9.-]+$')][string]$LdapHost,
    [ValidateRange(1, 65535)][int]$LdapPort = 636,
    [ValidateRange(1, 65535)][int]$PanelPort = 443,
    [ValidateRange(500, 30000)][int]$TimeoutMs = 4000,
    [string]$OutputPath,
    [switch]$IncludeNames
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$results = New-Object System.Collections.Generic.List[object]

function Hide([string]$Value) {
    if ($IncludeNames -or [string]::IsNullOrEmpty($Value)) { return $Value }
    return '<gizli>'
}

function Add-Result([string]$Id, [string]$Check, [ValidateSet('OK', 'UYARI', 'HATA', 'ATLANDI')][string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Id = $Id; Check = $Check; Status = $Status; Detail = $Detail })
}

function Test-Tcp([string]$TargetHost, [int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($TargetHost, $Port)
        if (-not $task.Wait($TimeoutMs)) { return 'zaman aşımı' }
        if ($client.Connected) { return $null }
        return 'bağlanılamadı'
    }
    catch {
        return $_.Exception.GetBaseException().Message
    }
    finally {
        $client.Dispose()
    }
}

# TLS el sıkışması: sertifika zinciri ve ad doğrulaması. Veri gönderilmez.
function Test-Tls([string]$TargetHost, [int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    $state = @{ Errors = 'None'; NotAfter = $null }
    try {
        $task = $client.ConnectAsync($TargetHost, $Port)
        if (-not $task.Wait($TimeoutMs)) { return @{ Error = 'zaman aşımı' } }
        $callback = [System.Net.Security.RemoteCertificateValidationCallback] {
            param($s, $certificate, $chain, $errors)
            $state.Errors = [string]$errors
            if ($certificate) { $state.NotAfter = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $certificate).NotAfter }
            return $true
        }
        $ssl = New-Object System.Net.Security.SslStream($client.GetStream(), $false, $callback)
        try {
            $ssl.AuthenticateAsClient($TargetHost)
        }
        finally {
            $ssl.Dispose()
        }
        return @{ Error = $null; PolicyErrors = $state.Errors; NotAfter = $state.NotAfter }
    }
    catch {
        return @{ Error = $_.Exception.GetBaseException().Message }
    }
    finally {
        $client.Dispose()
    }
}

function Add-TlsResult([string]$Id, [string]$Check, [string]$TargetHost, [int]$Port) {
    $tls = Test-Tls $TargetHost $Port
    if ($tls.Error) {
        Add-Result $Id $Check 'HATA' "TLS bağlantısı kurulamadı: $($tls.Error)"
    }
    elseif ($tls.PolicyErrors -ne 'None') {
        Add-Result $Id $Check 'HATA' "Sertifika bu makinede güvenilir değil veya ad uyuşmuyor ($($tls.PolicyErrors)). Kurum CA'sının kök sertifikasını kontrol edin."
    }
    elseif ($tls.NotAfter -and $tls.NotAfter -lt (Get-Date).AddDays(30)) {
        Add-Result $Id $Check 'UYARI' "Sertifika 30 gün içinde doluyor ($($tls.NotAfter.ToString('yyyy-MM-dd')))."
    }
    else {
        Add-Result $Id $Check 'OK' 'TLS el sıkışması ve sertifika doğrulaması başarılı.'
    }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$os = Get-CimInstance Win32_OperatingSystem
$build = [int]$os.BuildNumber

# ------------------------------------------------------------------ ortak
Add-Result 'C01' 'PowerShell sürümü' $(if ($PSVersionTable.PSVersion.Major -ge 5) { 'OK' } else { 'HATA' }) "PowerShell $($PSVersionTable.PSVersion)"
Add-Result 'C02' 'Yönetici yetkisi' $(if ($isAdmin) { 'OK' } else { 'UYARI' }) $(if ($isAdmin) { 'Yönetici olarak çalışıyor.' } else { 'Kurulum betikleri yönetici gerektirir; bazı kontroller eksik kalabilir.' })
Add-Result 'C03' 'İşletim sistemi' 'OK' "$($os.Caption) (build $build)"
$computer = Get-CimInstance Win32_ComputerSystem
Add-Result 'C04' 'Domain üyeliği' $(if ($computer.PartOfDomain) { 'OK' } else { 'UYARI' }) $(if ($computer.PartOfDomain) { "Domain: $(Hide $computer.Domain)" } else { 'Makine domain üyesi değil (Windows Authentication ve AD işlemleri için gerekir).' })

# ------------------------------------------------------------------ web
if ($Role -contains 'Web') {
    Add-Result 'W01' 'Windows Server 2019+' $(if ($build -ge 17763 -and $os.ProductType -ne 1) { 'OK' } else { 'HATA' }) $(if ($os.ProductType -eq 1) { 'İstemci işletim sistemi; web sunucusu Windows Server gerektirir.' } else { "build $build" })

    $w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
    Add-Result 'W02' 'IIS (W3SVC)' $(if ($w3svc) { 'OK' } else { 'HATA' }) $(if ($w3svc) { "Durum: $($w3svc.Status)" } else { "IIS kurulu değil: Install-WindowsFeature Web-Server -IncludeManagementTools" })

    $ancm = Join-Path $env:windir 'System32\inetsrv\aspnetcorev2.dll'
    if (Test-Path $ancm) {
        Add-Result 'W03' 'ASP.NET Core Module V2' 'OK' "Sürüm $((Get-Item $ancm).VersionInfo.FileVersion)"
    }
    else {
        Add-Result 'W03' 'ASP.NET Core Module V2' 'HATA' '.NET 9 Hosting Bundle kurulu değil (çevrimdışı kurulum dosyasını kullanın; kurulumdan sonra iisreset).'
    }

    Add-Result 'W04' 'WebAdministration modülü' $(if (Get-Module -ListAvailable WebAdministration) { 'OK' } else { 'HATA' }) 'Kurulum betiği IIS yapılandırması için kullanır.'

    if ($CertificateThumbprint) {
        $thumb = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
        $cert = Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $thumb }
        if (-not $cert) {
            Add-Result 'W05' 'HTTPS sertifikası' 'HATA' 'Parmak izine sahip sertifika LocalMachine\My deposunda yok.'
        }
        else {
            $problems = @()
            if (-not $cert.HasPrivateKey) { $problems += 'özel anahtar yok' }
            if ($cert.NotAfter -lt (Get-Date)) { $problems += 'süresi dolmuş' }
            $eku = @($cert.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId })
            if ($eku.Count -gt 0 -and $eku -notcontains '1.3.6.1.5.5.7.3.1') { $problems += 'Sunucu Kimlik Doğrulaması (EKU) yok' }
            if ($HostName) {
                $names = @($cert.DnsNameList | ForEach-Object { $_.Unicode })
                $match = $names | Where-Object { $_ -ieq $HostName -or ($_ -like '`*.*' -and $HostName -like $_) }
                if (-not $match) { $problems += 'HostName sertifikadaki adlarla (SAN) eşleşmiyor' }
            }
            if ($problems.Count -gt 0) {
                Add-Result 'W05' 'HTTPS sertifikası' 'HATA' ($problems -join '; ')
            }
            elseif ($cert.NotAfter -lt (Get-Date).AddDays(30)) {
                Add-Result 'W05' 'HTTPS sertifikası' 'UYARI' "30 gün içinde doluyor ($($cert.NotAfter.ToString('yyyy-MM-dd')))."
            }
            else {
                Add-Result 'W05' 'HTTPS sertifikası' 'OK' "Geçerlilik sonu $($cert.NotAfter.ToString('yyyy-MM-dd')), konu $(Hide $cert.Subject)"
            }
        }
    }
    else {
        Add-Result 'W05' 'HTTPS sertifikası' 'ATLANDI' '-CertificateThumbprint verilmedi.'
    }

    if ($SqlServer) {
        if ($SqlServer -match '\\') {
            Add-Result 'W06' 'SQL Server erişimi' 'ATLANDI' 'Adlandırılmış örnek (dinamik port). Bağlantıyı SSMS veya sqlcmd ile doğrulayın.'
        }
        else {
            $target = ($SqlServer -split ',')[0]
            $port = if ($SqlServer -match ',(\d+)$') { [int]$Matches[1] } else { $SqlPort }
            $err = Test-Tcp $target $port
            Add-Result 'W06' 'SQL Server erişimi (TCP)' $(if ($err) { 'HATA' } else { 'OK' }) $(if ($err) { "$(Hide $target):$port -> $err" } else { "$(Hide $target):$port açık. Login yetkisini ayrıca doğrulayın (docs/DEPLOYMENT.md)." })
        }
    }
    else {
        Add-Result 'W06' 'SQL Server erişimi' 'ATLANDI' '-SqlServer verilmedi.'
    }

    if ($LdapHost) {
        $err = Test-Tcp $LdapHost $LdapPort
        if ($err) {
            Add-Result 'W07' 'LDAPS erişimi' 'HATA' "$(Hide $LdapHost):$LdapPort -> $err"
        }
        elseif ($LdapPort -eq 636 -or $LdapPort -eq 3269) {
            Add-TlsResult 'W07' 'LDAPS erişimi ve sertifika' $LdapHost $LdapPort
        }
        else {
            Add-Result 'W07' 'LDAP erişimi' 'UYARI' "Port $LdapPort açık ama şifresiz LDAP. LDAPS (636) önerilir."
        }
    }
    else {
        Add-Result 'W07' 'LDAPS erişimi' 'ATLANDI' '-LdapHost verilmedi.'
    }

    Add-Result 'W08' 'ActiveDirectory modülü (RSAT)' $(if (Get-Module -ListAvailable ActiveDirectory) { 'OK' } else { 'UYARI' }) 'AD runbook''ları için gerekir: Install-WindowsFeature RSAT-AD-PowerShell'
    Add-Result 'W09' 'GroupPolicy modülü (GPMC)' $(if (Get-Module -ListAvailable GroupPolicy) { 'OK' } else { 'UYARI' }) 'GPO modülü için gerekir: Install-WindowsFeature GPMC'

    $drive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($env:ProgramData.Substring(0, 2))'"
    $freeGb = [math]::Round($drive.FreeSpace / 1GB, 1)
    Add-Result 'W10' 'Boş disk (ProgramData sürücüsü)' $(if ($freeGb -ge 5) { 'OK' } elseif ($freeGb -ge 2) { 'UYARI' } else { 'HATA' }) "$freeGb GB"

    if ($w3svc -and (Get-Module -ListAvailable WebAdministration)) {
        Import-Module WebAdministration
        $used = @(Get-WebBinding -ErrorAction SilentlyContinue | Where-Object { $_.protocol -eq 'https' -and $_.bindingInformation -like "*:$($PanelPort):*" })
        Add-Result 'W11' "IIS'te $PanelPort portu" $(if ($used.Count -eq 0) { 'OK' } else { 'UYARI' }) $(if ($used.Count -eq 0) { 'Boş.' } else { "$($used.Count) HTTPS bağlaması zaten bu portu kullanıyor (güncellemede beklenir; yeni kurulumda çakışma olabilir)." })
    }
}

# ------------------------------------------------------------------ agent / dashboard
if ($Role -contains 'Agent' -or $Role -contains 'Dashboard') {
    if ($HostName) {
        $err = Test-Tcp $HostName $PanelPort
        if ($err) {
            Add-Result 'A01' 'Panel erişimi (TCP)' 'HATA' "$(Hide $HostName):$PanelPort -> $err"
        }
        else {
            Add-TlsResult 'A02' 'Panel HTTPS sertifikası güveni' $HostName $PanelPort
        }
    }
    else {
        Add-Result 'A01' 'Panel erişimi' 'ATLANDI' '-HostName verilmedi.'
    }
}

if ($Role -contains 'Agent') {
    Add-Result 'A03' 'Windows sürümü (agent)' $(if ($build -ge 14393) { 'OK' } else { 'UYARI' }) "build $build (Windows Server 2016+ / Windows 10+ desteklenir)"
    $existing = Get-Service -Name SystemPanelAgent -ErrorAction SilentlyContinue
    Add-Result 'A04' 'Mevcut agent servisi' 'OK' $(if ($existing) { "Kurulu ($($existing.Status)); kurulum betiği güncelleme yapar." } else { 'Kurulu değil (yeni kurulum).' })
}

if ($Role -contains 'Dashboard') {
    $webView = $null
    foreach ($key in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
                     'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
                     'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') {
        $pv = (Get-ItemProperty -Path $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($pv -and $pv -ne '0.0.0.0') { $webView = $pv; break }
    }
    Add-Result 'D01' 'WebView2 Runtime' $(if ($webView) { 'OK' } else { 'HATA' }) $(if ($webView) { "Sürüm $webView" } else { 'Kurulu değil (Evergreen çevrimdışı kurulum dosyası kullanın).' })
}

# ------------------------------------------------------------------ rapor
$results | Format-Table -AutoSize -Wrap Id, Status, Check, Detail | Out-String -Width 220 | Write-Host

$summary = [pscustomobject]@{
    tool        = 'Test-SystemPanelPrerequisites'
    roles       = $Role
    generatedAt = (Get-Date).ToString('s')
    namesHidden = -not $IncludeNames
    counts      = [pscustomobject]@{
        ok      = @($results | Where-Object Status -eq 'OK').Count
        warning = @($results | Where-Object Status -eq 'UYARI').Count
        error   = @($results | Where-Object Status -eq 'HATA').Count
        skipped = @($results | Where-Object Status -eq 'ATLANDI').Count
    }
    results     = $results
}

if ($OutputPath) {
    $summary | ConvertTo-Json -Depth 4 | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host "Rapor yazıldı: $OutputPath"
}

Write-Host ("Sonuç: {0} OK, {1} uyarı, {2} hata, {3} atlandı" -f $summary.counts.ok, $summary.counts.warning, $summary.counts.error, $summary.counts.skipped)
if ($summary.counts.error -gt 0) { exit 1 }
exit 0
