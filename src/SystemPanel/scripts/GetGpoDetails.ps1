# Tek GPO detayı (JSON nesne).
param(
    [Parameter(Mandatory = $true)][guid]$Guid
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    $gpo = Get-GPO -Guid $Guid
    ConvertTo-Json -InputObject (ConvertTo-GpoRecord $gpo (Get-GpoLinkMap)) -Depth 6 -Compress
}
catch { Write-Failure $_.Exception.Message }
