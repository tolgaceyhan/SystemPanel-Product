<#
    SystemPanel runbook: ad.user.password-expiry-report (salt okunur)
    msDS-UserPasswordExpiryTimeComputed değerine göre parolası -DaysAhead gün içinde dolacak etkin kullanıcılar.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SearchBase,
    [Parameter(Mandatory = $true)][ValidateRange(1, 90)][int]$DaysAhead
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module ActiveDirectory

$now = Get-Date
$limit = $now.AddDays($DaysAhead)

$users = Get-ADUser -SearchBase $SearchBase -SearchScope Subtree `
    -Filter { Enabled -eq $true -and PasswordNeverExpires -eq $false } `
    -Properties 'msDS-UserPasswordExpiryTimeComputed', DisplayName, mail

$items = @($users | ForEach-Object {
    $raw = $_.'msDS-UserPasswordExpiryTimeComputed'
    if ($raw -and $raw -gt 0 -and $raw -lt [long]::MaxValue) {
        $expires = [DateTime]::FromFileTime($raw)
        if ($expires -ge $now -and $expires -le $limit) {
            [ordered]@{
                user        = $_.SamAccountName
                displayName = $_.DisplayName
                mail        = $_.mail
                expires     = $expires.ToString('yyyy-MM-dd HH:mm')
            }
        }
    }
} | Sort-Object { $_.expires } | Select-Object -First 2000)

[pscustomobject]@{
    searchBase = $SearchBase
    daysAhead  = $DaysAhead
    count      = $items.Count
    users      = $items
} | ConvertTo-Json -Depth 4
