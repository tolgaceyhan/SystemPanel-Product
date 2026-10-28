<#
    SystemPanel runbook: server.disk.temp-cleanup (agent)
    Yalnızca %SystemRoot%\Temp altında, -OlderThanDays günden eski dosyaları siler.
    Yeniden ayrıştırma noktaları (junction/symlink) izlenmez; kullanımdaki dosyalar atlanır.
    -DryRun: silinecek dosya sayısı ve toplam boyut, değişiklik yok.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateRange(7, 365)][int]$OlderThanDays,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'Temp'))
$cutoff = (Get-Date).AddDays(-$OlderThanDays)

function Get-OldFiles([string]$Directory) {
    $dirInfo = New-Object IO.DirectoryInfo($Directory)
    foreach ($entry in $dirInfo.EnumerateFileSystemInfos()) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if ($entry -is [IO.DirectoryInfo]) {
            Get-OldFiles -Directory $entry.FullName
        }
        elseif ($entry.LastWriteTime -lt $cutoff) {
            $entry
        }
    }
}

$files = @(Get-OldFiles -Directory $root | Where-Object { $_.FullName.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) })
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
if (-not $totalBytes) { $totalBytes = 0 }

if ($DryRun) {
    [pscustomobject]@{
        dryRun        = $true
        root          = $root
        olderThanDays = $OlderThanDays
        fileCount     = $files.Count
        totalMB       = [math]::Round($totalBytes / 1MB, 1)
        sample        = @($files | Select-Object -First 20 | ForEach-Object { $_.FullName })
    } | ConvertTo-Json -Depth 3
    exit 0
}

$deleted = 0
$freed = 0
$skipped = 0
foreach ($file in $files) {
    try {
        $length = $file.Length
        $file.Delete()
        $deleted++
        $freed += $length
    }
    catch {
        $skipped++
    }
}

[pscustomobject]@{
    dryRun   = $false
    root     = $root
    deleted  = $deleted
    skipped  = $skipped
    freedMB  = [math]::Round($freed / 1MB, 1)
} | ConvertTo-Json -Depth 3
