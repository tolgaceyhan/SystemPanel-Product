<#
    SystemPanel örnek runbook: org.example.ad-group-report (server, low)
    -SearchBase altındaki gruplar ve doğrudan üye sayıları. Yalnızca okuma yapar.
    -SearchBase'in kullanıcının yetki kapsamında olduğu SystemPanel tarafından talep ve onay sırasında doğrulanır.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SearchBase,
    [Parameter(Mandatory = $true)][ValidateRange(1, 5000)][int]$MaxGroups
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module ActiveDirectory

$groups = @(Get-ADGroup -SearchBase $SearchBase -SearchScope Subtree -Filter * -ResultSetSize $MaxGroups -Properties member, GroupScope, GroupCategory |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            name     = $_.Name
            dn       = $_.DistinguishedName
            scope    = [string]$_.GroupScope
            category = [string]$_.GroupCategory
            members  = @($_.member).Count
        }
    })

[pscustomobject]@{
    searchBase = $SearchBase
    count      = $groups.Count
    empty      = @($groups | Where-Object { $_.members -eq 0 }).Count
    truncated  = $groups.Count -ge $MaxGroups
    groups     = $groups
} | ConvertTo-Json -Depth 4
