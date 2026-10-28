# GPO'yu bir OU'ya bağlar.
param(
    [Parameter(Mandatory = $true)][guid]$Guid,
    [Parameter(Mandatory = $true)][string]$OuDn
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    New-GPLink -Guid $Guid -Target $OuDn -LinkEnabled Yes | Out-Null
    Write-Success @{ guid = $Guid.ToString(); target = $OuDn }
}
catch { Write-Failure $_.Exception.Message }
