# GPO bağlantısını bir OU'dan kaldırır (GPO'nun kendisi silinmez).
param(
    [Parameter(Mandatory = $true)][guid]$Guid,
    [Parameter(Mandatory = $true)][string]$OuDn
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    Remove-GPLink -Guid $Guid -Target $OuDn | Out-Null
    Write-Success @{ guid = $Guid.ToString(); target = $OuDn }
}
catch { Write-Failure $_.Exception.Message }
