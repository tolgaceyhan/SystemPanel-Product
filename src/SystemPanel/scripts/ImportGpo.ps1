# Yedekten mevcut bir GPO'ya içe aktarır (hedefin ayarlarının ÜZERİNE yazar).
# Uygulamada Gpo:AllowImport=true olmadıkça çağrılmaz.
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$BackupGpoName,
    [Parameter(Mandatory = $true)][string]$TargetName
)
. (Join-Path $PSScriptRoot 'GpoCommon.ps1')
try {
    $gpo = Import-GPO -BackupGpoName $BackupGpoName -Path $Path -TargetName $TargetName
    Write-Success @{ guid = $gpo.Id.ToString(); target = $TargetName }
}
catch { Write-Failure $_.Exception.Message }
