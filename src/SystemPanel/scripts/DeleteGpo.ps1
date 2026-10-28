# GPO'yu kalıcı olarak siler. Uygulamada Gpo:AllowDelete=true olmadıkça çağrılmaz.
param(
    [Parameter(Mandatory = $true)][guid]$Guid
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    Remove-GPO -Guid $Guid -Confirm:$false
    Write-Success @{ guid = $Guid.ToString() }
}
catch { Write-Failure $_.Exception.Message }
