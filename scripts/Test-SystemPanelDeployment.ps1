<#
.SYNOPSIS
    Kurulumdan SONRA SystemPanel web uygulamasını dışarıdan yoklar (salt okunur duman testi).

.DESCRIPTION
    Yalnızca anonim GET istekleri gönderir. Oturum açmaz, form göndermez, hiçbir veriyi değiştirmez.
    Kontroller:
      - /health/live 200 ve "Healthy"
      - HTTP isteğinin HTTPS'e yönlendirilmesi (80 portu açıksa)
      - Giriş sayfası: güvenlik başlıkları (CSP, HSTS, nosniff, X-Frame-Options), cookie bayrakları,
        harici (CDN) kaynak olmaması
      - Korunan uçların anonim erişime kapalı olması (/health/ready, otomasyon, ayarlar, agent API)
      - /setup sayfasının durumu (ilk kurulum tamamlandıysa 404 beklenir)

    Rapor varsayılan olarak sunucu adını gizler. Ayrıntılar: docs/FIELD-VALIDATION.md

.EXAMPLE
    .\Test-SystemPanelDeployment.ps1 -BaseUrl https://panel.kurum.local -OutputPath .\duman.json

.EXAMPLE
    # İlk kurulumdan hemen önce (setup sayfası henüz açıkken)
    .\Test-SystemPanelDeployment.ps1 -BaseUrl https://panel.kurum.local -ExpectSetupOpen
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^https://[A-Za-z0-9.-]+(:\d+)?/?$')][string]$BaseUrl,
    [switch]$ExpectSetupOpen,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 15,
    [string]$OutputPath,
    [switch]$IncludeNames
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
Add-Type -AssemblyName System.Net.Http

$BaseUrl = $BaseUrl.TrimEnd('/')
$baseUri = [Uri]$BaseUrl
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Id, [string]$Check, [ValidateSet('OK', 'UYARI', 'HATA', 'ATLANDI')][string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Id = $Id; Check = $Check; Status = $Status; Detail = $Detail })
}

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)

function Get-Url([string]$Url) {
    try {
        $response = $client.GetAsync($Url).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{ Error = $null; Status = [int]$response.StatusCode; Response = $response; Body = $body }
    }
    catch {
        return [pscustomobject]@{ Error = $_.Exception.GetBaseException().Message; Status = 0; Response = $null; Body = $null }
    }
}

function Get-Header($Response, [string]$Name) {
    $values = $null
    if ($Response.Headers.TryGetValues($Name, [ref]$values)) { return ($values -join ', ') }
    if ($Response.Content.Headers.TryGetValues($Name, [ref]$values)) { return ($values -join ', ') }
    return $null
}

function Test-Denied([string]$Id, [string]$Path) {
    $r = Get-Url "$BaseUrl$Path"
    if ($r.Error) {
        Add-Result $Id "Anonim erişim kapalı: $Path" 'HATA' $r.Error
    }
    elseif ($r.Status -in 401, 403) {
        Add-Result $Id "Anonim erişim kapalı: $Path" 'OK' "$($r.Status)"
    }
    elseif ($r.Status -in 301, 302, 303, 307, 308) {
        $location = [string]$r.Response.Headers.Location
        $status = if ($location -match '(?i)/Account/Login') { 'OK' } else { 'UYARI' }
        Add-Result $Id "Anonim erişim kapalı: $Path" $status "$($r.Status) -> giriş sayfası"
    }
    else {
        Add-Result $Id "Anonim erişim kapalı: $Path" 'HATA' "Beklenmeyen yanıt $($r.Status); uç anonim erişime açık olabilir."
    }
}

try {
    # ------------------------------------------------------------------ canlılık
    $live = Get-Url "$BaseUrl/health/live"
    if ($live.Error) {
        Add-Result 'S01' '/health/live' 'HATA' "Bağlantı kurulamadı: $($live.Error). Sertifika bu makinede güvenilir mi? (Test-SystemPanelPrerequisites -Role Agent)"
    }
    elseif ($live.Status -eq 200 -and $live.Body -match 'Healthy') {
        Add-Result 'S01' '/health/live' 'OK' '200 Healthy'
    }
    else {
        Add-Result 'S01' '/health/live' 'HATA' "$($live.Status). IIS günlüklerine ve %ProgramData%\SystemPanel\logs altına bakın (500.30 = uygulama başlatılamadı)."
    }

    # ------------------------------------------------------------------ HTTP -> HTTPS
    $httpUrl = "http://$($baseUri.Host)/health/live"
    $http = Get-Url $httpUrl
    if ($http.Error) {
        Add-Result 'S02' 'HTTP -> HTTPS yönlendirmesi' 'ATLANDI' '80 portu kapalı veya erişilemiyor (yalnızca HTTPS yayını; sorun değil).'
    }
    elseif ($http.Status -in 301, 302, 307, 308 -and ([string]$http.Response.Headers.Location) -like 'https://*') {
        Add-Result 'S02' 'HTTP -> HTTPS yönlendirmesi' 'OK' "$($http.Status)"
    }
    else {
        Add-Result 'S02' 'HTTP -> HTTPS yönlendirmesi' 'HATA' "HTTP isteği HTTPS'e yönlendirilmedi ($($http.Status)). Security:RequireHttps kapalı olabilir."
    }

    # ------------------------------------------------------------------ giriş sayfası ve başlıklar
    $login = Get-Url "$BaseUrl/Account/Login"
    if ($login.Error -or $login.Status -ne 200) {
        Add-Result 'S03' 'Giriş sayfası' 'HATA' $(if ($login.Error) { $login.Error } else { "$($login.Status)" })
    }
    else {
        Add-Result 'S03' 'Giriş sayfası' 'OK' '200'

        $csp = Get-Header $login.Response 'Content-Security-Policy'
        Add-Result 'S04' 'Content-Security-Policy' $(if ($csp -and $csp -match "default-src 'self'") { 'OK' } else { 'HATA' }) $(if ($csp) { $csp } else { 'yok' })

        $hsts = Get-Header $login.Response 'Strict-Transport-Security'
        Add-Result 'S05' 'HSTS' $(if ($hsts) { 'OK' } else { 'UYARI' }) $(if ($hsts) { $hsts } else { 'yok (Security:RequireHttps=false veya ara cihaz başlığı siliyor)' })

        $nosniff = Get-Header $login.Response 'X-Content-Type-Options'
        $frame = Get-Header $login.Response 'X-Frame-Options'
        Add-Result 'S06' 'nosniff / X-Frame-Options' $(if ($nosniff -eq 'nosniff' -and $frame) { 'OK' } else { 'HATA' }) "X-Content-Type-Options=$nosniff; X-Frame-Options=$frame"

        $server = Get-Header $login.Response 'Server'
        $powered = Get-Header $login.Response 'X-Powered-By'
        Add-Result 'S07' 'Sürüm bilgisi sızıntısı' $(if (-not $powered) { 'OK' } else { 'UYARI' }) $(if ($powered) { "X-Powered-By=$powered (IIS'te kaldırılması önerilir)" } else { "Server=$(if ($server) { $server } else { 'yok' })" })

        $cookies = @()
        $values = $null
        if ($login.Response.Headers.TryGetValues('Set-Cookie', [ref]$values)) { $cookies = @($values) }
        $weak = @($cookies | Where-Object { $_ -notmatch '(?i);\s*secure' -or $_ -notmatch '(?i);\s*httponly' })
        if ($cookies.Count -eq 0) {
            Add-Result 'S08' 'Cookie bayrakları' 'ATLANDI' 'Giriş sayfası cookie göndermedi.'
        }
        else {
            Add-Result 'S08' 'Cookie bayrakları' $(if ($weak.Count -eq 0) { 'OK' } else { 'HATA' }) $(if ($weak.Count -eq 0) { "$($cookies.Count) cookie: Secure + HttpOnly" } else { "$($weak.Count) cookie Secure/HttpOnly değil: " + (($weak | ForEach-Object { ($_ -split '=')[0] }) -join ', ') })
        }

        # Kapalı ağ: sayfa harici kaynak yüklememeli.
        $external = @([regex]::Matches($login.Body, '(?i)<(script|link|img)[^>]+(src|href)\s*=\s*["''](https?:)?//(?<host>[^/"'']+)') |
            ForEach-Object { $_.Groups['host'].Value } |
            Where-Object { $_ -ne $baseUri.Authority -and $_ -ne $baseUri.Host } |
            Select-Object -Unique)
        Add-Result 'S09' 'Harici (CDN) kaynak yok' $(if ($external.Count -eq 0) { 'OK' } else { 'HATA' }) $(if ($external.Count -eq 0) { 'Tüm kaynaklar uygulamanın kendisinden.' } else { "$($external.Count) harici adres" })
    }

    # ------------------------------------------------------------------ korunan uçlar
    Test-Denied 'S10' '/health/ready'
    Test-Denied 'S11' '/automation'
    Test-Denied 'S12' '/Audit'
    $agentApi = Get-Url "$BaseUrl/api/v1/agents/commands/next"
    # GET tanımlı değil (405) veya kimlik yok (401) kabul edilir; 200 kabul edilmez.
    Add-Result 'S13' 'Agent API anonim erişime kapalı' $(if ($agentApi.Status -in 401, 403, 404, 405) { 'OK' } else { 'HATA' }) "$($agentApi.Status)"

    # ------------------------------------------------------------------ kurulum sihirbazı
    $setup = Get-Url "$BaseUrl/setup"
    if ($ExpectSetupOpen) {
        Add-Result 'S14' 'İlk kurulum sihirbazı açık' $(if ($setup.Status -eq 200) { 'OK' } else { 'HATA' }) "$($setup.Status) (belirteç: %ProgramData%\SystemPanel\setup-token.txt)"
    }
    else {
        Add-Result 'S14' 'İlk kurulum sihirbazı kapalı' $(if ($setup.Status -eq 404) { 'OK' } elseif ($setup.Status -eq 200) { 'UYARI' } else { 'UYARI' }) $(if ($setup.Status -eq 200) { 'Sihirbaz hâlâ açık: ilk yöneticiyi oluşturun (kurulum öncesindeyseniz -ExpectSetupOpen kullanın).' } else { "$($setup.Status)" })
    }
}
finally {
    $client.Dispose()
}

# ------------------------------------------------------------------ rapor
$results | Format-Table -AutoSize -Wrap Id, Status, Check, Detail | Out-String -Width 220 | Write-Host

$summary = [pscustomobject]@{
    tool        = 'Test-SystemPanelDeployment'
    target      = $(if ($IncludeNames) { $BaseUrl } else { '<gizli>' })
    generatedAt = (Get-Date).ToString('s')
    counts      = [pscustomobject]@{
        ok      = @($results | Where-Object Status -eq 'OK').Count
        warning = @($results | Where-Object Status -eq 'UYARI').Count
        error   = @($results | Where-Object Status -eq 'HATA').Count
        skipped = @($results | Where-Object Status -eq 'ATLANDI').Count
    }
    results     = $results
}

if ($OutputPath) {
    $summary | ConvertTo-Json -Depth 4 | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host "Rapor yazıldı: $OutputPath"
}

Write-Host ("Sonuç: {0} OK, {1} uyarı, {2} hata, {3} atlandı" -f $summary.counts.ok, $summary.counts.warning, $summary.counts.error, $summary.counts.skipped)
if ($summary.counts.error -gt 0) { exit 1 }
exit 0
