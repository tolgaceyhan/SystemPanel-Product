#Requires -Version 7.0
<#
.SYNOPSIS
    SystemPanel sürüm paketini üretir (Windows derleme makinesinde veya CI'da).

.DESCRIPTION
    Çıktı: artifacts\SystemPanel-<sürüm>.zip ve SHA256SUMS.txt
      web\        IIS'e kurulacak web uygulaması (self-contained, win-x64)
      agent\      İzleme agent'ı (self-contained, win-x64)
      dashboard\  NOC ekranı (self-contained, win-x64)
      database\   migrate.sql (idempotent; DBA için) + efbundle.exe (otomatik uygulama için)
      scripts\    Kurulum/kaldırma ve yardımcı betikler
      tools\      sprunbook.exe (kurum runbook'larını imzalama aracı; lisans üretme yeteneği yoktur)
      runbook-examples\  Kurum runbook'ları için örnek kaynaklar (imzasız; kurum kendi anahtarıyla imzalar)
      docs\       Kurulum belgeleri, THIRD-PARTY-NOTICES.md, appsettings.Production.example.json

    -SigningCertThumbprint verilirse üretilen .exe/.dll dosyaları signtool ile imzalanır
    (sertifika derleme makinesinin sertifika deposunda olmalı; özel anahtar repoya girmez).

    -RunbookSigningKeyPath verilirse runbooks/src altındaki yerleşik runbook'lar imzalanıp web\runbooks\
    altına konur (açılışta kataloğa alınır). Ürünün bunlara güvenmesi için eşleşen açık anahtar
    src/SystemPanel/Automation/runbook-vendor-public.pem olarak repoda bulunmalıdır.

.EXAMPLE
    pwsh ./build/Build-Release.ps1 -Version 1.0.0-preview.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..' 'artifacts'),
    [switch]$SkipTests,
    [string]$SigningCertThumbprint,
    [string]$TimestampUrl,
    [string]$RunbookSigningKeyPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$name = "SystemPanel-$Version"
$stage = Join-Path $OutputRoot $name
$numericVersion = ($Version -split '-')[0]

# Bilinçli olarak param() bloğu yok: "-o", "-c" gibi dotnet argümanları PowerShell parametresi
# olarak yorumlanmadan $args ile olduğu gibi aktarılır.
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') başarısız (çıkış kodu $LASTEXITCODE)." }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Push-Location $repo
try {
    Invoke-Dotnet tool restore

    if (-not $SkipTests) {
        Invoke-Dotnet test SystemPanel.sln -c Release
    }

    $common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Version", "-p:FileVersion=$numericVersion.0", "-p:AssemblyVersion=$numericVersion.0")

    Invoke-Dotnet publish src/SystemPanel/SystemPanel.csproj @common -o (Join-Path $stage 'web')
    Invoke-Dotnet publish src/AgentSystemPanel/AgentSystemPanel.csproj @common -o (Join-Path $stage 'agent')
    Invoke-Dotnet publish src/SystemPanel.Dashboard/SystemPanel.Dashboard.csproj @common -o (Join-Path $stage 'dashboard')

    # Yerleşik runbook kataloğu (ADR-002)
    if ($RunbookSigningKeyPath) {
        if (-not (Test-Path src/SystemPanel/Automation/runbook-vendor-public.pem)) {
            throw 'runbook-vendor-public.pem repoda yok: imzalanan runbook''lara ürün güvenmez.'
        }
        Invoke-Dotnet run --project tools/SystemPanel.LicenseTool -c Release -- runbook-sign-all --key $RunbookSigningKeyPath --source runbooks/src --out (Join-Path $stage 'web' 'runbooks')
    }
    else {
        Write-Warning 'Yerleşik runbook''lar pakete eklenmedi (-RunbookSigningKeyPath verilmedi).'
    }

    # Geliştirme yapılandırmaları pakete girmez.
    Get-ChildItem (Join-Path $stage 'web') -Filter 'appsettings.*.json' |
        Where-Object { $_.Name -ne 'appsettings.json' } | Remove-Item -Force

    $db = New-Item -ItemType Directory -Path (Join-Path $stage 'database')
    Invoke-Dotnet ef migrations script --idempotent --project src/SystemPanel --output (Join-Path $db 'migrate.sql')
    Invoke-Dotnet ef migrations bundle --project src/SystemPanel -r win-x64 --self-contained --force --output (Join-Path $db 'efbundle.exe')

    # Kurum runbook aracı ve örnekleri (ADR-002 Faz 4). splicense (lisans üretici) pakete GİRMEZ.
    Invoke-Dotnet publish tools/SystemPanel.RunbookTool/SystemPanel.RunbookTool.csproj @common -p:PublishSingleFile=true -o (Join-Path $stage 'tools')
    Copy-Item runbooks/examples (Join-Path $stage 'runbook-examples') -Recurse

    $scripts = New-Item -ItemType Directory -Path (Join-Path $stage 'scripts')
    Copy-Item scripts/*.ps1 $scripts

    $docs = New-Item -ItemType Directory -Path (Join-Path $stage 'docs')
    Copy-Item docs/*.md, README.md, CHANGELOG.md, THIRD-PARTY-NOTICES.md $docs
    Copy-Item src/SystemPanel/appsettings.Production.example.json $docs
    Copy-Item src/SystemPanel.Dashboard/appsettings.example.json (Join-Path $docs 'dashboard.appsettings.example.json')

    if ($SigningCertThumbprint) {
        $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if (-not $signtool) { throw 'signtool.exe bulunamadı (Windows SDK gerekli).' }
        $targets = Get-ChildItem $stage -Recurse -Include 'SystemPanel*.exe', 'SystemPanel*.dll', 'sprunbook.exe', 'efbundle.exe'
        $signArgs = @('sign', '/sha1', $SigningCertThumbprint, '/fd', 'SHA256')
        if ($TimestampUrl) { $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256') }
        & $signtool.Source @signArgs @($targets.FullName)
        if ($LASTEXITCODE -ne 0) { throw 'İmzalama başarısız.' }
    }
    else {
        Write-Warning 'Dosyalar imzalanmadı (-SigningCertThumbprint verilmedi). Müşteriye giden paketler imzalanmalıdır.'
    }

    $zip = Join-Path $OutputRoot "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content (Join-Path $OutputRoot 'SHA256SUMS.txt') -Encoding ascii

    Write-Host "Paket: $zip"
    Write-Host "SHA256: $hash"
}
finally {
    Pop-Location
}
