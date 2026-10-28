# Örnek kurum runbook'ları

Bu klasördeki runbook'lar **imzasızdır** ve ürünle birlikte kataloğa yüklenmez. Kurumlar bunları başlangıç noktası
olarak kopyalar, kendi kimliklerini verir (`org.<kurum>.…`), gözden geçirir, test eder ve **kendi anahtarlarıyla**
imzalar. Ayrıntılar: [docs/RUNBOOK-DEVELOPMENT.md](../../docs/RUNBOOK-DEVELOPMENT.md).

| Kimlik | Çalıştırıcı | Risk | Ne yapar | Gösterdiği kalıp |
|---|---|---|---|---|
| `org.example.certificate-expiry-report` | agent | low | Yerel sertifika deposunda süresi yaklaşan sertifikaları listeler | Salt okunur agent raporu, `enum` + `int` parametre |
| `org.example.ad-group-report` | server | low | Bir OU altındaki grupları üye sayılarıyla listeler | `ad-dn` + `inScope`, salt okunur AD raporu |
| `org.example.ad-user-unlock` | server | medium | Kilitlenen kullanıcı hesabının kilidini açar | Değişiklik yapan AD işlemi, `-DryRun` önizlemesi, ayrıcalıklı hesap koruması |
| `org.example.iis-apppool-recycle` | agent | medium | IIS uygulama havuzunu geri dönüştürür | `pattern` ile sınırlanmış ad, önizleme, idempotent işlem |

Kullanım:

```powershell
# 1) Kopyala ve yeniden adlandır (klasör adı = kimlik)
Copy-Item runbook-examples\org.example.ad-user-unlock runbooks\org.kurum.ad-user-unlock -Recurse
# manifest.json içinde "id": "org.kurum.ad-user-unlock"

# 2) Denetle, imzala, doğrula
sprunbook lint     --source runbooks
sprunbook sign-all --key <özel anahtar yolu> --source runbooks --out paketler
sprunbook verify   --public runbook-org-public.pem --package paketler\org.kurum.ad-user-unlock.sprb

# 3) SystemPanel > Otomasyon > Katalog ve anahtarlar > Runbook paketi içe aktar
```
