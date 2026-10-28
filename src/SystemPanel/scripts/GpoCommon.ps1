# SystemPanel GPO betikleri için ortak yardımcılar.
# Betikler YALNIZCA param() ile girdi alır ve sonuçlarını JSON olarak stdout'a yazar.
# Web uygulaması bunları "powershell.exe -File <betik> -Param deger" şeklinde çalıştırır;
# değerler hiçbir zaman PowerShell kodu olarak değerlendirilmez.

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Import-Module GroupPolicy -ErrorAction Stop

function Write-Success([hashtable]$Data = @{}) {
    $Data['success'] = $true
    ConvertTo-Json -InputObject $Data -Depth 6 -Compress
}

function Write-Failure([string]$Message) {
    ConvertTo-Json -InputObject @{ success = $false; error = $Message } -Compress
    exit 1
}

# GPO GUID (küçük harf) -> bağlı hedef DN listesi. Tüm gPLink sahiplerini (OU, domain kökü,
# site) TEK seferde okur; önceki sürüm her GPO için tüm OU'ları tarıyor ve adı regex olarak
# eşliyordu (yanlış eşleşme + O(n*m)).
function Get-GpoLinkMap {
    Import-Module ActiveDirectory -ErrorAction Stop

    $map = @{}
    $domain = Get-ADDomain
    $holders = @()
    $holders += @(Get-ADOrganizationalUnit -Filter * -Properties gPLink)
    $holders += @(Get-ADObject -Identity $domain.DistinguishedName -Properties gPLink)
    $holders += @(Get-ADObject -LDAPFilter '(objectClass=site)' -SearchBase ("CN=Sites," + (Get-ADRootDSE).configurationNamingContext) -Properties gPLink)

    foreach ($h in $holders) {
        if (-not $h.gPLink) { continue }
        foreach ($m in [regex]::Matches([string]$h.gPLink, '\{([0-9a-fA-F\-]{36})\}')) {
            $g = $m.Groups[1].Value.ToLowerInvariant()
            if (-not $map.ContainsKey($g)) { $map[$g] = New-Object System.Collections.ArrayList }
            [void]$map[$g].Add([string]$h.DistinguishedName)
        }
    }
    return $map
}

function ConvertTo-GpoRecord($Gpo, [hashtable]$LinkMap) {
    $links = @()
    $key = $Gpo.Id.ToString().ToLowerInvariant()
    if ($LinkMap.ContainsKey($key)) { $links = @($LinkMap[$key]) }

    $type = 'None'
    if ($Gpo.User.Enabled -and $Gpo.Computer.Enabled) { $type = 'All' }
    elseif ($Gpo.User.Enabled) { $type = 'User' }
    elseif ($Gpo.Computer.Enabled) { $type = 'Computer' }

    [PSCustomObject]@{
        Id           = $Gpo.Id.ToString()
        DisplayName  = $Gpo.DisplayName
        Description  = $Gpo.Description
        Owner        = $Gpo.Owner
        # PS 5.1 ConvertTo-Json tarihleri "\/Date(...)\/" yazar; .NET tarafı ISO 8601 bekler.
        CreatedTime  = $Gpo.CreationTime.ToString('o')
        ModifiedTime = $Gpo.ModificationTime.ToString('o')
        DomainName   = $Gpo.DomainName
        # Sayısal enum değeri yerine ad: AllSettingsEnabled, AllSettingsDisabled, UserSettingsDisabled, ComputerSettingsDisabled
        GpoStatus    = [string]$Gpo.GpoStatus
        GpoVersion   = "U:$($Gpo.User.DSVersion) C:$($Gpo.Computer.DSVersion)"
        GpoPath      = $Gpo.Path
        GpoType      = $type
        LinkedOus    = @($links | Where-Object { $_ -notlike '*,CN=Sites,*' })
        LinkedSites  = @($links | Where-Object { $_ -like '*,CN=Sites,*' })
    }
}
