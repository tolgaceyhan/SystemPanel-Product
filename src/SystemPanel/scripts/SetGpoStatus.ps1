# GPO durumunu değiştirir. (Önceki kod var olmayan "Set-GPO -Status" parametresini kullanıyordu.)
param(
    [Parameter(Mandatory = $true)][guid]$Guid,
    [Parameter(Mandatory = $true)]
    [ValidateSet('AllSettingsEnabled', 'AllSettingsDisabled', 'UserSettingsDisabled', 'ComputerSettingsDisabled')]
    [string]$Status
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    $gpo = Get-GPO -Guid $Guid
    $before = [string]$gpo.GpoStatus
    $gpo.GpoStatus = [Microsoft.GroupPolicy.GpoStatus]$Status
    Write-Success @{ guid = $Guid.ToString(); before = $before; after = $Status }
}
catch { Write-Failure $_.Exception.Message }
