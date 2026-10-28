<#
    SystemPanel runbook: server.diagnostics.snapshot (agent, salt okunur)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 50)][int]$TopProcesses
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$os = Get-CimInstance -ClassName Win32_OperatingSystem
$uptime = (Get-Date) - $os.LastBootUpTime

$disks = @(Get-CimInstance -ClassName Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
    [ordered]@{
        drive   = $_.DeviceID
        sizeGB  = [math]::Round($_.Size / 1GB, 1)
        freeGB  = [math]::Round($_.FreeSpace / 1GB, 1)
        freePct = $(if ($_.Size) { [math]::Round(100 * $_.FreeSpace / $_.Size, 1) } else { $null })
    }
})

$processes = @(Get-Process | Sort-Object -Property WorkingSet64 -Descending | Select-Object -First $TopProcesses | ForEach-Object {
    [ordered]@{
        name     = $_.ProcessName
        id       = $_.Id
        memoryMB = [math]::Round($_.WorkingSet64 / 1MB, 0)
        cpuSec   = $(if ($_.CPU) { [math]::Round($_.CPU, 0) } else { $null })
    }
})

$pendingReboot = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                 (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')

$criticalEvents = 0
try {
    $criticalEvents = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 1, 2; StartTime = (Get-Date).AddDays(-1) } -MaxEvents 500 -ErrorAction Stop).Count
}
catch [Exception] {
    if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $criticalEvents = $null }
}

[pscustomobject]@{
    computer          = $env:COMPUTERNAME
    os                = $os.Caption
    version           = $os.Version
    uptimeHours       = [math]::Round($uptime.TotalHours, 1)
    pendingReboot     = $pendingReboot
    criticalEvents24h = $criticalEvents
    disks             = $disks
    topProcesses      = $processes
} | ConvertTo-Json -Depth 4
