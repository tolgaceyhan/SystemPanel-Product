<#
    SystemPanel runbook: ad.computer.stale-report (salt okunur)
    LastLogonDate değeri -Days günden eski olan bilgisayarları listeler (en fazla 2000 kayıt).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SearchBase,
    [Parameter(Mandatory = $true)][ValidateRange(30, 730)][int]$Days
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module ActiveDirectory

$cutoff = (Get-Date).AddDays(-$Days)
$computers = Get-ADComputer -SearchBase $SearchBase -SearchScope Subtree -Filter { LastLogonTimeStamp -lt $cutoff -or LastLogonTimeStamp -notlike '*' } `
    -Properties LastLogonDate, OperatingSystem, Enabled |
    Sort-Object LastLogonDate |
    Select-Object -First 2000

$items = @($computers | ForEach-Object {
    [ordered]@{
        name            = $_.Name
        enabled         = $_.Enabled
        lastLogon       = $(if ($_.LastLogonDate) { $_.LastLogonDate.ToString('yyyy-MM-dd') } else { $null })
        operatingSystem = $_.OperatingSystem
        dn              = $_.DistinguishedName
    }
})

[pscustomobject]@{
    searchBase = $SearchBase
    days       = $Days
    count      = $items.Count
    computers  = $items
} | ConvertTo-Json -Depth 4
