# Tüm GPO'ları bağlantılarıyla birlikte listeler (JSON dizi).
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    $linkMap = Get-GpoLinkMap
    $result = @(Get-GPO -All | Sort-Object DisplayName | ForEach-Object { ConvertTo-GpoRecord $_ $linkMap })
    ConvertTo-Json -InputObject $result -Depth 6 -Compress
}
catch { Write-Failure $_.Exception.Message }
