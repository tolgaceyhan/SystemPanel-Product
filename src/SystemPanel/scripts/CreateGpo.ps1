param(
    [Parameter(Mandatory=$true)]
    [string]$Name,

    [string]$Description = "",

    [string]$OuDn = "",

    [string]$TemplatePath = "",

    [bool]$Enabled = $true,

    # LGPO.exe yolu (önceden sabit bir C: yoluydu)
    [string]$LgpoPath = ""
)

# -----------------------------
# 1) GPO oluşturma
# -----------------------------
try {
    $gpo = New-GPO -Name $Name -Comment $Description -ErrorAction Stop
}
catch {
    Write-Output "{""success"": false, ""error"": ""GPO oluşturulamadı: $($_.Exception.Message)""}"
    exit 1
}

# -----------------------------
# 2) OU’ya linkleme
# -----------------------------
if ($OuDn -ne "") {
    try {
        New-GPLink -Name $Name -Target $OuDn -ErrorAction Stop | Out-Null
    }
    catch {
        Write-Output "{""success"": false, ""error"": ""GPO OU'ya linklenemedi: $($_.Exception.Message)""}"
        exit 1
    }
}

# -----------------------------
# 3) Şablon / Policy dosyası varsa işleme
# -----------------------------
if ($TemplatePath -ne "" -and (Test-Path $TemplatePath)) {

    $ext = [System.IO.Path]::GetExtension($TemplatePath).ToLower()

    if ($ext -eq ".xml") {
        # XML backup import
        try {
            $backupFolder = Split-Path $TemplatePath
            Import-GPO -BackupGpoName $Name -Path $backupFolder -TargetName $Name -ErrorAction Stop
        }
        catch {
            Write-Output "{""success"": false, ""error"": ""XML içe aktarılamadı: $($_.Exception.Message)""}"
            exit 1
        }
    }
    elseif ($ext -eq ".inf" -or $ext -eq ".pol") {
        # LGPO ile import edilecek
        $lgpo = $LgpoPath
        if ($lgpo -ne "" -and (Test-Path -LiteralPath $lgpo)) {
            Start-Process $lgpo -ArgumentList "/g `"$Name`" /m `"$TemplatePath`"" -NoNewWindow -Wait
        }
    }
}

# -----------------------------
# 4) GPO enable/disable
# -----------------------------
try {
    if ($Enabled -eq $false) {
        Set-GPO -Guid $gpo.Id -UserEnabled:$false -ComputerEnabled:$false
    }
    else {
        Set-GPO -Guid $gpo.Id -UserEnabled:$true -ComputerEnabled:$true
    }
}
catch { }

Write-Output "{""success"": true, ""id"": ""$($gpo.Id)""}"
exit 0
