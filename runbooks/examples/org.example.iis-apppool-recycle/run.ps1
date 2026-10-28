<#
    SystemPanel örnek runbook: org.example.iis-apppool-recycle (agent, medium)
    Uygulama havuzunu geri dönüştürür. Durdurulmuş havuz başlatılmaz (beklenmeyen hizmet açılmasını önler).
    -DryRun: değişiklik yapmadan havuz durumunu döndürür.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9 ._-]{1,64}$')][string]$AppPoolName,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module WebAdministration

$poolPath = Join-Path 'IIS:\AppPools' $AppPoolName
if (-not (Test-Path -LiteralPath $poolPath)) {
    throw "Uygulama havuzu bulunamadı: $AppPoolName"
}

$state = (Get-WebAppPoolState -Name $AppPoolName).Value
$workers = @(Get-ChildItem -LiteralPath (Join-Path $poolPath 'WorkerProcesses') -ErrorAction SilentlyContinue).Count
$plan = [ordered]@{
    appPool        = $AppPoolName
    state          = $state
    workerProcesses = $workers
    action         = $(if ($state -eq 'Started') { 'recycle' } else { 'none (havuz çalışmıyor)' })
}

if ($DryRun) {
    [pscustomobject]@{ dryRun = $true; plan = $plan } | ConvertTo-Json -Depth 4
    exit 0
}

if ($state -eq 'Started') {
    Restart-WebAppPool -Name $AppPoolName
}

[pscustomobject]@{ dryRun = $false; done = $plan; stateAfter = (Get-WebAppPoolState -Name $AppPoolName).Value } | ConvertTo-Json -Depth 4
