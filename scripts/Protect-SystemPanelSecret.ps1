<#
.SYNOPSIS
    SystemPanel / Agent yapılandırması için bir sırrı DPAPI (LocalMachine) ile şifreler.

.DESCRIPTION
    Çıktı "dpapi:<base64>" biçimindedir ve appsettings.Production.json içinde ya da ortam
    değişkeni olarak (ör. ConnectionStrings__DefaultConnection) doğrudan kullanılabilir.
    Değer YALNIZCA bu betiğin çalıştırıldığı makinede çözülebilir; her sunucu için ayrı üretin.
    Düz metin sır ekrana yazılmaz, komut geçmişine düşmemesi için Read-Host -AsSecureString kullanılır.

.EXAMPLE
    .\Protect-SystemPanelSecret.ps1
    # İstenen değeri girin; çıktıyı yapılandırmaya yapıştırın.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$secure = Read-Host -Prompt 'Korunacak değer' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $bytes = [Text.Encoding]::UTF8.GetBytes($plain)
    $entropy = [Text.Encoding]::UTF8.GetBytes('SystemPanel.Configuration.v1')
    $protected = [Security.Cryptography.ProtectedData]::Protect($bytes, $entropy, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    'dpapi:' + [Convert]::ToBase64String($protected)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Variable plain, bytes -ErrorAction SilentlyContinue
}
