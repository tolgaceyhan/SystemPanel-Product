# SystemPanel — Otomasyon (Runbook'lar)

Tasarım ve gerekçeler: [ADR-002](ADR/002-automation-runbooks.md). Bu belge kullanım ve işletim kılavuzudur.

## Temel kurallar

- **Yalnızca imzalı runbook'lar çalışır.** Kullanıcı betik yazamaz; katalogdan bir runbook seçip formdaki parametreleri doldurur.
- **Parametreler doğrulanır:**
  - Türler sabit bir listeden gelir: metin, sayı, evet/hayır, liste, AD DN, sunucu adı.
  - AD nesneleri kullanıcının **yetki kapsamında** olmalıdır.
  - Değerler betiğe kod olarak değil, ayrı argüman olarak geçer.
- **Dört göz:**
  - Orta ve yüksek riskli runbook'lar ikinci bir yetkilinin onayını gerektirir.
  - Talep eden kişi kendi talebini onaylayamaz.
  - Onaylayan kişinin de hedef üzerinde yetkisi olmalıdır.
- **Önizleme:** Yüksek riskli runbook'lar önce değişiklik yapmadan ne yapacağını gösterir (dry-run). Önizleme başarısız olursa talep çalıştırılmaz.
- **Her adım denetim kaydına yazılır:** `automation.run.requested` (önizleme sonucu dahil), `.approved`, `.rejected`, `.cancelled`, `.expired`, `.started`, `.completed`.
- Çıktılarda parola, anahtar ve token gibi alanlar maskelenir.

## İzinler

| İzin | Ne sağlar |
|---|---|
| `automation.view` | Katalog ve (görebildiği) çalıştırmalar |
| `automation.run` | Çalıştırma talebi oluşturma, kendi talebini iptal |
| `automation.approve` | Başkasının talebini onaylama/reddetme |
| `automation.manage` | Paket içe aktarma, sürüm etkin/devre dışı, imzalama anahtarları |

Varsayılan olarak yalnızca **Admin** bu izinlere sahiptir. **İzinler** ekranından ayrı roller için verin. Dört göz için talep eden ve onaylayan farklı kişiler olmalıdır.

**Görünürlük:**
- Kapsamı global olan kullanıcılar (`scope.all`) tüm çalıştırmaları görür.
- Diğerleri yalnızca şunları görür: kendi talepleri, karar verdikleri talepler ve hedefi kendi kapsamındaki talepler.

## Akış

```
Talep ──► (önizleme) ──► Onay bekliyor ──► Kuyrukta ──► Çalışıyor ──► Başarılı / Başarısız / Zaman aşımı
              │                  ├──► Reddedildi
              └► Önizleme        ├──► İptal edildi (talep eden)
                 başarısız       └──► Süresi doldu (varsayılan 24 saat)
```

- Düşük riskli runbook'lar onaysız kuyruğa girer.
- Kuyruk arka planda sırayla işlenir; aynı anda bir iş çalışır.
- Uygulama yeniden başlarsa yarıda kalan iş **Başarısız** olarak işaretlenir ve otomatik olarak tekrar çalıştırılmaz. Hedefi kontrol edin.

## Yerleşik runbook'lar

| Runbook | Risk | Ne yapar |
|---|---|---|
| `ad.user.offboard` | Yüksek | Hesabı devre dışı bırakır. İsteğe bağlı: tüm gruplardan çıkarır, arşiv OU'suna taşır. Ayrıcalıklı (`adminCount=1`) hesapları işlemez |
| `ad.computer.stale-report` | Düşük | Verilen gün sayısından uzun süredir oturum açmamış bilgisayarları listeler (salt okunur) |
| `ad.user.password-expiry-report` | Düşük | Parolası yakında dolacak etkin kullanıcıları listeler (salt okunur) |
| `server.service.restart` (agent) | Orta | Hedef sunucuda bir servisi yeniden başlatır. Kritik sistem servislerini ve agent'ın kendisini reddeder. Önizleme durumu ve bağımlı servisleri gösterir |
| `server.disk.temp-cleanup` (agent) | Yüksek | Yalnızca `%SystemRoot%\Temp` altında N günden eski dosyaları siler. Junction/symlink izlemez. Önizleme dosya sayısını ve boyutunu gösterir |
| `server.diagnostics.snapshot` (agent) | Düşük | Çalışma süresi, diskler, en çok bellek kullanan süreçler, bekleyen yeniden başlatma, son 24 saatin kritik olayları (salt okunur) |

Kaynakları `runbooks/src/` altındadır. Sürüm paketi üretilirken üretici anahtarıyla imzalanır ve açılışta kataloğa alınır.

## Sunucu gereksinimleri

- Windows PowerShell 5.1 ve **ActiveDirectory** modülü: `Install-WindowsFeature RSAT-AD-PowerShell`.
- Runbook'lar web uygulamasının kimliğiyle çalışır (IIS uygulama havuzu). AD'de değişiklik yapan runbook'lar için bu kimliğe **yalnızca gereken OU'larda** yetki devredin (Delegation of Control).
  - Varsayılan `ApplicationPoolIdentity`, AD'ye bilgisayar hesabıyla erişir.
  - Daha iyi kontrol için uygulama havuzunu bir **gMSA** ile çalıştırın.
- Runbook betikleri çalıştırma sırasında `%ProgramData%\SystemPanel\Automation\work` klasörüne geçici olarak yazılır. Klasöre yalnızca Administrators, SYSTEM ve uygulama kimliği erişebilir. Betik yazıldıktan sonra özeti yeniden doğrulanır ve çalıştırmadan sonra silinir.

Ayarlar (`Automation:*`):

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `CatalogDirectory` | `<uygulama>\runbooks` | Açılışta içe aktarılan imzalı paketler |
| `ApprovalExpiryHours` | 24 | Onay bekleme süresi |
| `PowerShellPath` | `powershell.exe` | |
| `WorkDirectory` | `%ProgramData%\SystemPanel\Automation\work` | |
| `MaxOutputBytes` | 65536 | Saklanan çıktı sınırı |
| `PollSeconds` | 5 | Kuyruk kontrol aralığı |

Kuyruğu kapatmak için: `BackgroundJobs:Automation:Enabled=false`. Talepler oluşturulur ama çalıştırılmaz.

## Kurumun kendi runbook'ları

1. Anahtar çifti üretin (çevrimdışı, güvenli bir makinede):
   ```bash
   splicense runbook-keygen --out <güvenli-klasör>
   ```
2. **Açık** anahtarı kaydedin: **Otomasyon → Katalog ve anahtarlar → Kurum imzalama anahtarı ekle**.
3. Runbook'u yazın: `manifest.json` + `run.ps1` (örnekler için `runbooks/src/` klasörüne bakın). Kurallar:
   - `param()` bloğunda manifestteki her parametre bulunur. Evet/hayır parametreleri `[switch]` olur.
   - `supportsDryRun: true` ise betik `[switch]$DryRun` alır ve bu modda **hiçbir değişiklik yapmadan** planı JSON olarak yazar. Yüksek riskte önizleme zorunludur.
   - Sonucu JSON olarak stdout'a yazın. Hata durumunda çıkış kodu 0 olmamalıdır (`$ErrorActionPreference = 'Stop'`).
   - Sürüm her değişiklikte artırılmalıdır. Aynı sürüm farklı içerikle içe aktarılamaz.
4. İmzalayın ve içe aktarın:
   ```bash
   splicense runbook-sign --key <runbook-signing.private.pem> --source <runbook-klasörü> --out <ad>.sprb
   ```
   **Katalog ve anahtarlar → Runbook paketi içe aktar.**
5. Bir anahtar sızarsa **İptal et** deyin. O anahtarla imzalı tüm runbook'lar hemen çalıştırılamaz hale gelir (kuyruktakiler dahil).

## Agent üzerinde çalışan runbook'lar (Faz 2)

Manifestte `"executor": "agent"` ve `"targetType": "server"` olan runbook'lar web sunucusunda değil, seçilen sunucudaki **SystemPanel Agent** tarafından çalıştırılır.

```
Talep (hedef sunucu seçilir) ──► Önizleme bekliyor ──► agent önizlemeyi alır ve çalıştırır (-DryRun)
   ──► Onay bekliyor ──► onay (dört göz) ──► Kuyrukta ──► agent işi alır ──► Agent'ta çalışıyor ──► sonuç
```

- **Bağlantı yönü:** Sunucu agent'a bağlanmaz. Agent kendi işini düzenli aralıklarla sunucudan çeker (`POST /api/v1/agents/commands/next`). Güvenlik duvarında ek port açılmaz.
- **Hedef kapsamı:**
  - Kapsamı global olan kullanıcılar tüm agent'ları hedefleyebilir.
  - Diğerleri yalnızca birimi (agent kaydındaki "İl/Birim") kendi birimleriyle eşleşen agent'ları hedefleyebilir.
  - Birimi atanmamış agent'ı yalnızca global kapsamlı kullanıcılar hedefleyebilir.
  - Onaylayan kişi için de aynı kural geçerlidir.
- **Agent'ın kendi kontrolleri:** Agent sunucuya körü körüne güvenmez. Bir iş ancak şu koşulların hepsi sağlanırsa çalışır:
  - Otomasyon agent'ta açıkça etkinleştirilmiş olmalı (varsayılan **kapalı**).
  - Runbook kimliği agent'ın **yerel izin listesinde** olmalı.
  - Paket, agent'ın **kendi güvendiği** bir anahtarla imzalanmış olmalı (gömülü üretici anahtarı veya `TrustedKeyFiles`).
  - Parametreler şemaya uymalı; bilinmeyen parametre bulunmamalı.
  
  Bu yüzden web sunucusu ele geçirilse bile agent'a izin listesi dışında bir iş yaptırılamaz. Reddedilen iş hiç çalışmaz ve sonuç ekranında "Agent işi reddetti" olarak görünür.
- **Zaman aşımı:**
  - Agent işi zamanında almazsa (sunucu kapalı, otomasyon kapalı) iş **Süresi doldu** olur.
  - Agent işi alıp sonucu son tarihe kadar (betik süresi + 5 dk) bildirmezse iş **Zaman aşımı** olur.
  - Bu durumdaki işler yeniden çalıştırılmaz; hedef sunucuyu kontrol edin.

### Agent'ta otomasyonu açmak

Kurulum betiğiyle (yönetici PowerShell):

```powershell
.\Install-SystemPanelAgent.ps1 -ServerUrl https://<sunucu> -AgentId <guid> `
    -AllowedRunbooks server.diagnostics.snapshot, server.service.restart `
    -TrustedRunbookKeyFiles .\kurum-runbook-public.pem    # yalnızca kurum anahtarı kullanılıyorsa
```

Ya da `%ProgramData%\SystemPanel\Agent\agentsettings.json` içinde:

```json
"Automation": {
  "Enabled": true,
  "AllowedRunbooks": [ "server.diagnostics.snapshot", "server.service.restart" ],
  "TrustedKeyFiles": [ "kurum-runbook-public.pem" ]
}
```

Değişiklikten sonra servisi yeniden başlatın: `Restart-Service SystemPanelAgent`.

### Agent hesabının yetkileri

Agent düşük yetkili `NT SERVICE\SystemPanelAgent` hesabıyla çalışır ve **yetki yükseltmez**. Runbook'lar bu hesabın yapabildiği kadarını yapabilir. Yetkiyi yalnızca gereken kaynağa verin:

| Runbook | Gereken yetki | Örnek |
|---|---|---|
| `server.diagnostics.snapshot` | Ek yetki gerekmez (olay günlüğü için `Event Log Readers`) | `Add-LocalGroupMember -Group 'Event Log Readers' -Member 'NT SERVICE\SystemPanelAgent'` |
| `server.service.restart` | Yalnızca ilgili servis için başlatma/durdurma | `sc.exe sdshow <Servis>` çıktısına `(A;;RPWPDTLO;;;<agent SID>)` ekleyip `sc.exe sdset <Servis> <yeni SDDL>`. SID için: `sc.exe showsid SystemPanelAgent` |
| `server.disk.temp-cleanup` | `%SystemRoot%\Temp` üzerinde silme yetkisi | Klasör ACL'sine agent hesabı için `Modify`. Başka hesapların dosyaları için yönetici yetkisi gerekebilir; o dosyalar atlanır ve sonuçta "skipped" olarak sayılır |

Agent'ı `LocalSystem` olarak çalıştırmak tüm runbook'lara tam yetki verir. **Önerilmez.**

## Sınırlar ve sonraki adımlar

- Zamanlama ve olay tetikleyicileri yok (Faz 3).
- Sunucu tarafında ve her agent'ta aynı anda tek iş çalışıyor.
- Yeniden deneme yok. Başarısız işi yeni talep olarak başlatın.
