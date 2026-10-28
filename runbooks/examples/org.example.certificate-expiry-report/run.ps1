<#
    SystemPanel örnek runbook: org.example.certificate-expiry-report (agent, low)
    Cert:\LocalMachine\<Store> altında süresi -DaysAhead gün içinde dolacak (veya dolmuş) sertifikalar.
    Yalnızca okuma yapar; özel anahtarlara erişmez ve çıktıya yazmaz.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 365)][int]$DaysAhead,
    [Parameter(Mandatory = $true)][ValidateSet('My', 'WebHosting', 'Root', 'CA')][string]$Store
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$now = Get-Date
$limit = $now.AddDays($DaysAhead)
$path = Join-Path 'Cert:\LocalMachine' $Store

$items = @(Get-ChildItem -Path $path |
    Where-Object { $_.NotAfter -le $limit } |
    Sort-Object NotAfter |
    ForEach-Object {
        [pscustomobject]@{
            subject    = $_.Subject
            issuer     = $_.Issuer
            thumbprint = $_.Thumbprint
            notAfter   = $_.NotAfter.ToString('yyyy-MM-dd')
            daysLeft   = [int][math]::Floor(($_.NotAfter - $now).TotalDays)
            expired    = $_.NotAfter -lt $now
        }
    })

[pscustomobject]@{
    store     = $Store
    daysAhead = $DaysAhead
    count     = $items.Count
    expired   = @($items | Where-Object { $_.expired }).Count
    items     = $items
} | ConvertTo-Json -Depth 4
