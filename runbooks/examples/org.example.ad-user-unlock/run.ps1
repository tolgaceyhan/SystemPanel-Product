<#
    SystemPanel örnek runbook: org.example.ad-user-unlock (server, medium)
    Kilitli hesabın kilidini açar; parola değiştirilmez ve çıktıya yazılmaz.
    -DryRun: değişiklik yapmadan hesabın durumunu ve yapılacak işlemi döndürür.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UserDn,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module ActiveDirectory

$user = Get-ADUser -Identity $UserDn -Properties LockedOut, adminCount, SamAccountName, LastBadPasswordAttempt, BadLogonCount
if ($user.adminCount -eq 1) {
    throw 'Ayrıcalıklı (adminCount=1) hesaplar bu runbook ile işlenemez.'
}

$plan = [ordered]@{
    user                   = $user.SamAccountName
    dn                     = $user.DistinguishedName
    lockedOut              = [bool]$user.LockedOut
    badLogonCount          = $user.BadLogonCount
    lastBadPasswordAttempt = $(if ($user.LastBadPasswordAttempt) { $user.LastBadPasswordAttempt.ToString('yyyy-MM-dd HH:mm') } else { $null })
    action                 = $(if ($user.LockedOut) { 'unlock' } else { 'none' })
}

if ($DryRun) {
    [pscustomobject]@{ dryRun = $true; plan = $plan } | ConvertTo-Json -Depth 4
    exit 0
}

if ($user.LockedOut) {
    Unlock-ADAccount -Identity $user
}

[pscustomobject]@{ dryRun = $false; done = $plan } | ConvertTo-Json -Depth 4
