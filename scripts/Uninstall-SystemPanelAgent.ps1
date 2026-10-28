<#
.SYNOPSIS
    SystemPanel Agent servisini kaldırır. -RemoveData ile makineye özel ayarları ve tamponu da siler.
    Not: Agent'ın SystemPanel'deki kaydı ayrıca "Devre dışı bırak" ile kapatılmalıdır.
#>
[CmdletBinding()]
param(
    [string]$InstallPath = (Join-Path $env:ProgramFiles 'SystemPanel\Agent'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'SystemPanelAgent'

$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    & sc.exe delete $serviceName | Out-Null
}

if (Test-Path $InstallPath) { Remove-Item -Path $InstallPath -Recurse -Force }

if ($RemoveData) {
    $dataDir = Join-Path $env:ProgramData 'SystemPanel\Agent'
    if (Test-Path $dataDir) { Remove-Item -Path $dataDir -Recurse -Force }
}

Write-Host 'SystemPanel Agent kaldırıldı.'
