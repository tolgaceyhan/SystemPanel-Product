<#
.SYNOPSIS
    SystemPanel IIS sitesini ve uygulama havuzunu kaldırır.

.DESCRIPTION
    Veritabanına, AD'ye ve %ProgramData%\SystemPanel altındaki verilere (lisans, DataProtection anahtarları,
    dosya deposu) DOKUNMAZ. Uygulama dosyaları yalnızca -RemoveFiles ile silinir; appsettings.Production.json
    o durumda bile yedeklenir.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [ValidatePattern('^[A-Za-z0-9 ._-]+$')][string]$SiteName = 'SystemPanel',
    [string]$InstallPath = (Join-Path $env:ProgramFiles 'SystemPanel\Web'),
    [switch]$RemoveFiles
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bu betik yönetici olarak çalıştırılmalıdır.'
}

Import-Module WebAdministration

if ((Test-Path "IIS:\Sites\$SiteName") -and $PSCmdlet.ShouldProcess($SiteName, 'IIS sitesini kaldır')) {
    Stop-WebSite -Name $SiteName -ErrorAction SilentlyContinue
    Remove-WebSite -Name $SiteName
}
if ((Test-Path "IIS:\AppPools\$SiteName") -and $PSCmdlet.ShouldProcess($SiteName, 'Uygulama havuzunu kaldır')) {
    Stop-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
    Remove-WebAppPool -Name $SiteName
}

if ($RemoveFiles -and (Test-Path $InstallPath) -and $PSCmdlet.ShouldProcess($InstallPath, 'Uygulama dosyalarını sil')) {
    $config = Join-Path $InstallPath 'appsettings.Production.json'
    if (Test-Path $config) {
        $backup = Join-Path $env:ProgramData ("SystemPanel\appsettings.Production.{0:yyyyMMddHHmmss}.json.bak" -f (Get-Date))
        Copy-Item $config $backup
        Write-Host "Yapılandırma yedeklendi: $backup"
    }
    Remove-Item $InstallPath -Recurse -Force
}

Write-Host 'Tamamlandı. Veritabanı ve %ProgramData%\SystemPanel verileri korunmuştur.'
