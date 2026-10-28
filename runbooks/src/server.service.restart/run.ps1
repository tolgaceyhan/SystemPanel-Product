<#
    SystemPanel runbook: server.service.restart (agent)
    Bir Windows servisini yeniden başlatır. -DryRun: durum ve bağımlı servisler, değişiklik yok.
    Agent hesabının bu servis için başlatma/durdurma yetkisi olmalıdır (bkz. docs/AUTOMATION.md).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_.$-]{1,80}$')][string]$ServiceName,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# Yeniden başlatılması sistemi veya agent'ı durdurabilecek servisler.
$protected = @(
    'SystemPanelAgent', 'lsass', 'csrss', 'smss', 'wininit', 'winlogon', 'services', 'RpcSs', 'RpcEptMapper',
    'DcomLaunch', 'SamSs', 'EventLog', 'Netlogon', 'NTDS', 'Kdc', 'LSM', 'BFE', 'mpssvc', 'WinDefend', 'CryptSvc'
)
if ($protected -contains $ServiceName) {
    throw "Korunan servis yeniden başlatılamaz: $ServiceName"
}

$service = Get-Service -Name $ServiceName
$info = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($service.Name)'"
$plan = [ordered]@{
    service           = $service.Name
    displayName       = $service.DisplayName
    status            = $service.Status.ToString()
    startType         = $info.StartMode
    dependentServices = @($service.DependentServices | Where-Object { $_.Status -eq 'Running' } | ForEach-Object { $_.Name })
}

if ($DryRun) {
    [pscustomobject]@{ dryRun = $true; plan = $plan } | ConvertTo-Json -Depth 4
    exit 0
}

if ($info.StartMode -eq 'Disabled') {
    throw "Servis devre dışı (Disabled); yeniden başlatılmadı: $ServiceName"
}

Restart-Service -Name $service.Name -Force
$service.WaitForStatus('Running', [TimeSpan]::FromSeconds(120))
$service.Refresh()

[pscustomobject]@{
    dryRun  = $false
    service = $service.Name
    status  = $service.Status.ToString()
    at      = (Get-Date).ToString('s')
} | ConvertTo-Json -Depth 3
