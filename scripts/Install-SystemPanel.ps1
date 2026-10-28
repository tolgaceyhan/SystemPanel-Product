<#
.SYNOPSIS
    SystemPanel web uygulamasını IIS'e kurar veya günceller.

.DESCRIPTION
    Sürüm paketinin (SystemPanel-<sürüm>.zip) açıldığı klasördeki scripts\ altından, yönetici olarak çalıştırılır.

    Yapılanlar:
      1. Ön koşul kontrolü: yönetici yetkisi, IIS, ASP.NET Core Module V2 (Hosting Bundle).
      2. Uygulama havuzu (No Managed Code, ApplicationPoolIdentity) ve site (HTTPS) oluşturma.
      3. Dosyaların kopyalanması. Güncellemede mevcut appsettings.Production.json ve logs korunur.
      4. %ProgramData%\SystemPanel klasörü ve dosya deposu için uygulama havuzu kimliğine yazma izni.
      5. appsettings.Production.json yoksa oluşturulur. Yalnızca sır OLMAYAN değerler yazılır; SQL için
         Windows Authentication kullanılır. LDAP parolası gibi sırlar Protect-SystemPanelSecret.ps1 ile eklenir.
      6. -ApplyDatabaseMigrations verilirse veritabanı şeması güncellenir (onay istenir). Verilmezse
         database\migrate.sql dosyasının DBA tarafından uygulanması beklenir.
      7. Site başlatılır ve /health/live kontrol edilir. İlk kurulumda /setup adresi ve kurulum belirteci
         dosyasının yeri yazdırılır.

    Betik veritabanını, AD'yi veya mevcut verileri SİLMEZ.

.EXAMPLE
    .\Install-SystemPanel.ps1 -HostName panel.kurum.local -CertificateThumbprint 0123ABCD... -SqlServer sql01 -Database SystemPanel

.EXAMPLE
    # Güncelleme (yapılandırma korunur), şemayı da uygula:
    .\Install-SystemPanel.ps1 -HostName panel.kurum.local -CertificateThumbprint 0123ABCD... -SqlServer sql01 -ApplyDatabaseMigrations
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9.-]+$')][string]$HostName,
    [string]$CertificateThumbprint,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9._\\,-]+$')][string]$SqlServer,
    [ValidatePattern('^[A-Za-z0-9_]+$')][string]$Database = 'SystemPanel',
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$InstallPath = (Join-Path $env:ProgramFiles 'SystemPanel\Web'),
    [ValidatePattern('^[A-Za-z0-9 ._-]+$')][string]$SiteName = 'SystemPanel',
    [ValidateRange(1, 65535)][int]$Port = 443,
    [switch]$AllowHttp,
    [switch]$ApplyDatabaseMigrations
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$appPool = $SiteName
$appPoolIdentity = "IIS AppPool\$appPool"
$source = Join-Path $PackageRoot 'web'
$dataRoot = Join-Path $env:ProgramData 'SystemPanel'
$prodConfig = Join-Path $InstallPath 'appsettings.Production.json'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Grant-Modify([string]$Path, [string]$Identity) {
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
    $acl = Get-Acl $Path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($Identity, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -Path $Path -AclObject $acl
}

# ---------------------------------------------------------------- 1. Ön koşullar
Write-Step 'Ön koşullar kontrol ediliyor'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bu betik yönetici olarak çalıştırılmalıdır.'
}
if (-not (Get-Service -Name W3SVC -ErrorAction SilentlyContinue)) {
    throw 'IIS (Web-Server rolü) kurulu değil. Install-WindowsFeature Web-Server -IncludeManagementTools'
}
if (-not (Test-Path (Join-Path $env:windir 'System32\inetsrv\aspnetcorev2.dll'))) {
    throw 'ASP.NET Core Module V2 bulunamadı. .NET 9 Hosting Bundle kurulmalıdır (çevrimdışı kurulum dosyasıyla).'
}
if (-not (Test-Path (Join-Path $source 'SystemPanel.dll'))) {
    throw "Paket klasöründe web uygulaması bulunamadı: $source"
}
if (-not $AllowHttp) {
    if (-not $CertificateThumbprint) { throw '-CertificateThumbprint gerekli (veya geçici olarak -AllowHttp).' }
    $cert = Get-Item -Path "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) { throw "Sertifika LocalMachine\My deposunda bulunamadı: $CertificateThumbprint" }
    if ($cert.NotAfter -lt (Get-Date)) { throw "Sertifikanın süresi dolmuş: $($cert.NotAfter)" }
}
else {
    Write-Warning '-AllowHttp: oturum çerezleri ve parolalar ağda şifresiz taşınır. Yalnızca geçici kullanım içindir.'
}

Import-Module WebAdministration

# ---------------------------------------------------------------- 2. Uygulama havuzu ve site
$isUpgrade = Test-Path "IIS:\Sites\$SiteName"

if ($PSCmdlet.ShouldProcess($SiteName, $(if ($isUpgrade) { 'IIS sitesini güncelle' } else { 'IIS sitesi oluştur' }))) {

    if ($isUpgrade) {
        Write-Step 'Mevcut site durduruluyor (güncelleme)'
        Stop-WebSite -Name $SiteName -ErrorAction SilentlyContinue
        if (Test-Path "IIS:\AppPools\$appPool") { Stop-WebAppPool -Name $appPool -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
    }

    if (-not (Test-Path "IIS:\AppPools\$appPool")) {
        Write-Step "Uygulama havuzu oluşturuluyor: $appPool"
        New-WebAppPool -Name $appPool | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$appPool" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$appPool" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty "IIS:\AppPools\$appPool" -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty "IIS:\AppPools\$appPool" -Name startMode -Value 'AlwaysRunning'

    # ------------------------------------------------------------ 3. Dosyalar
    Write-Step "Dosyalar kopyalanıyor: $InstallPath"
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    # /MIR yerine /E: kurulum klasöründeki appsettings.Production.json ve logs korunur.
    & robocopy.exe $source $InstallPath /E /NFL /NDL /NJH /NJS /NP /XF appsettings.Production.json | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Dosya kopyalama başarısız (robocopy çıkış kodu $LASTEXITCODE)." }

    if (-not $isUpgrade) {
        Write-Step "Site oluşturuluyor: $SiteName"
        if ($AllowHttp) {
            New-WebSite -Name $SiteName -PhysicalPath $InstallPath -ApplicationPool $appPool -HostHeader $HostName -Port $Port | Out-Null
        }
        else {
            New-WebSite -Name $SiteName -PhysicalPath $InstallPath -ApplicationPool $appPool -HostHeader $HostName -Port $Port -Ssl -SslFlags 1 | Out-Null
            $binding = Get-WebBinding -Name $SiteName -Protocol https -HostHeader $HostName -Port $Port
            $binding.AddSslCertificate($CertificateThumbprint, 'My')
        }
    }

    # ------------------------------------------------------------ 4. İzinler
    Write-Step 'Klasör izinleri ayarlanıyor'
    Grant-Modify -Path $dataRoot -Identity $appPoolIdentity
    Grant-Modify -Path (Join-Path $InstallPath 'logs') -Identity $appPoolIdentity
    $fileStore = Join-Path $dataRoot 'Files'
    Grant-Modify -Path $fileStore -Identity $appPoolIdentity

    # ------------------------------------------------------------ 5. Yapılandırma
    if (-not (Test-Path $prodConfig)) {
        Write-Step 'appsettings.Production.json oluşturuluyor (sır içermez)'
        $config = [ordered]@{
            ConnectionStrings = [ordered]@{
                DefaultConnection = "Server=$SqlServer;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;"
            }
            Security = [ordered]@{ RequireHttps = (-not $AllowHttp) }
            FileStorage = [ordered]@{ Root = $fileStore }
        }
        $config | ConvertTo-Json -Depth 5 | Set-Content -Path $prodConfig -Encoding UTF8
        $acl = Get-Acl $prodConfig
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($id in @('BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'Allow')))
        }
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($appPoolIdentity, 'Read', 'Allow')))
        Set-Acl -Path $prodConfig -AclObject $acl
    }
    else {
        Write-Step 'Mevcut appsettings.Production.json korunuyor'
    }
}

# ---------------------------------------------------------------- 6. Veritabanı
$connection = "Server=$SqlServer;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;"
$bundle = Join-Path $PackageRoot 'database\efbundle.exe'
if ($ApplyDatabaseMigrations) {
    if (-not (Test-Path $bundle)) { throw "Migration paketi bulunamadı: $bundle" }
    $prompt = "$SqlServer / $Database veritabanına şema güncellemeleri uygulanacak. Önce YEDEK aldığınızdan emin olun."
    if ($PSCmdlet.ShouldProcess("$SqlServer/$Database", 'Veritabanı şemasını güncelle') -and
        $PSCmdlet.ShouldContinue($prompt, 'Veritabanı güncellemesi')) {
        Write-Step 'Veritabanı şeması güncelleniyor (bu betiği çalıştıran hesabın yetkisiyle)'
        & $bundle --connection $connection
        if ($LASTEXITCODE -ne 0) { throw "Veritabanı güncellemesi başarısız (çıkış kodu $LASTEXITCODE)." }
    }
}
else {
    Write-Warning "Veritabanı şeması uygulanmadı. DBA'nız database\migrate.sql dosyasını uygulamalı veya betiği -ApplyDatabaseMigrations ile çalıştırın."
}

# ---------------------------------------------------------------- 7. Başlat ve kontrol et
if ($PSCmdlet.ShouldProcess($SiteName, 'Siteyi başlat')) {
    Write-Step 'Site başlatılıyor'
    Start-WebAppPool -Name $appPool
    Start-WebSite -Name $SiteName

    $scheme = $(if ($AllowHttp) { 'http' } else { 'https' })
    $portSuffix = $(if (($scheme -eq 'https' -and $Port -eq 443) -or ($scheme -eq 'http' -and $Port -eq 80)) { '' } else { ":$Port" })
    $baseUrl = "$($scheme)://$HostName$portSuffix"

    try {
        $resp = Invoke-WebRequest -Uri "$baseUrl/health/live" -UseBasicParsing -TimeoutSec 60
        Write-Host "Sağlık kontrolü: $($resp.StatusCode) $($resp.Content)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Sağlık kontrolü yapılamadı ($baseUrl/health/live): $($_.Exception.Message). Logs: $InstallPath\logs"
    }

    Write-Host ''
    Write-Host 'Sonraki adımlar:' -ForegroundColor Yellow
    Write-Host "  1. SQL Server'da uygulama havuzu kimliğine erişim verin. SQL ayrı sunucudaysa bilgisayar hesabı"
    Write-Host "     ($env:USERDOMAIN\$env:COMPUTERNAME`$) için, aynı sunucudaysa '$appPoolIdentity' için login + db_datareader/db_datawriter."
    Write-Host "  2. Sırları ekleyin: scripts\Protect-SystemPanelSecret.ps1 (ör. Ldap:BindPassword) -> $prodConfig"
    Write-Host "  3. İlk kurulum: $baseUrl/setup  (belirteç: $dataRoot\setup-token.txt)"
    Write-Host "  4. Lisans: SystemPanel > Lisans ekranından .lic dosyasını yükleyin."
}
