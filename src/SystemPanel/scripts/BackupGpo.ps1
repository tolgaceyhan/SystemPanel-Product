# GPO yedeği alır.
param(
    [Parameter(Mandatory = $true)][guid]$Guid,
    [Parameter(Mandatory = $true)][string]$Path
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
    $backup = Backup-GPO -Guid $Guid -Path $Path
    Write-Success @{ guid = $Guid.ToString(); backupId = $backup.Id.ToString(); path = $Path }
}
catch { Write-Failure $_.Exception.Message }
