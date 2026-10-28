# Bir OU'ya doğrudan bağlı GPO'lar (JSON dizi).
param(
    [Parameter(Mandatory = $true)][string]$OuDn
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    $links = @((Get-GPInheritance -Target $OuDn).GpoLinks | ForEach-Object {
        [PSCustomObject]@{
            GpoId       = $_.GpoId.ToString()
            DisplayName = $_.DisplayName
            Enabled     = [bool]$_.Enabled
            Enforced    = [bool]$_.Enforced
            Order       = [int]$_.Order
        }
    })
    ConvertTo-Json -InputObject $links -Depth 4 -Compress
}
catch { Write-Failure $_.Exception.Message }
