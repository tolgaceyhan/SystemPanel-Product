<#
    SystemPanel runbook: ad.user.offboard
    Hesabı devre dışı bırakır, isteğe bağlı olarak gruplardan çıkarır ve arşiv OU'suna taşır.
    -DryRun: hiçbir değişiklik yapmadan planı JSON olarak döndürür.
    Hedefin yetki kapsamı SystemPanel tarafından talep ve onay sırasında doğrulanır.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UserDn,
    [string]$ArchiveOu,
    [switch]$RemoveGroups,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module ActiveDirectory

$user = Get-ADUser -Identity $UserDn -Properties MemberOf, Enabled, adminCount, SamAccountName
if ($user.adminCount -eq 1) {
    throw 'Ayrıcalıklı (adminCount=1) hesaplar bu runbook ile işlenemez.'
}
if ($ArchiveOu) {
    $null = Get-ADOrganizationalUnit -Identity $ArchiveOu
}

$groups = @($user.MemberOf)
$plan = [ordered]@{
    user         = $user.SamAccountName
    dn           = $user.DistinguishedName
    disable      = [bool]$user.Enabled
    removeGroups = $(if ($RemoveGroups) { $groups } else { @() })
    moveTo       = $(if ($ArchiveOu) { $ArchiveOu } else { $null })
}

if ($DryRun) {
    [pscustomobject]@{ dryRun = $true; plan = $plan } | ConvertTo-Json -Depth 5
    exit 0
}

if ($user.Enabled) {
    Disable-ADAccount -Identity $user
}
if ($RemoveGroups) {
    foreach ($group in $groups) {
        Remove-ADGroupMember -Identity $group -Members $user -Confirm:$false
    }
}
if ($ArchiveOu -and ($user.DistinguishedName -notlike "*,$ArchiveOu")) {
    Move-ADObject -Identity $user.DistinguishedName -TargetPath $ArchiveOu
}

[pscustomobject]@{ dryRun = $false; done = $plan } | ConvertTo-Json -Depth 5
