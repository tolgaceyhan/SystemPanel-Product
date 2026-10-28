# Gerçek Ortam Doğrulama Planı

Amaç: SystemPanel'i ilk kez gerçek bir Windows, Active Directory, SQL Server ve IIS ortamında uçtan uca denemek ve
otomatik testlerin yakalayamayacağı sorunları bulmak. Yazılımın şimdiye kadarki testleri Linux üzerinde SQLite ve
taklit (fake) AD/PowerShell ile yapıldı. Bu plan o boşluğu kapatır.

> **Kurallar**
> - **Yalnızca izole test ortamı.** Üretim domain'i, üretim SQL'i ve gerçek kullanıcılar kullanılmaz. Mümkünse sanal makine anlık görüntüsü (snapshot) alın.
> - Test domain'inde de yıkıcı AD işlemleri (taşıma, devre dışı bırakma, grup çıkarma) yalnızca bu plan için oluşturulan **test OU'larında** yapılır.
> - Sonuçları paylaşırken parola, anahtar, sertifika parmak izi, gerçek sunucu, domain ve kişi adı **göndermeyin**. Betikler adları varsayılan olarak gizler; ekran görüntülerinde adları karartın.
> - Test için üretilen anahtarlar (runbook, lisans) **yalnızca test içindir**. Müşteriye giden yapıya girmez; test bitince silinir.

## 1. Test ortamı

| Makine | Rol | En az |
|---|---|---|
| `DC01` | Test domain'inin etki alanı denetleyicisi (AD DS + DNS + AD CS veya kurum CA'sı) | Windows Server 2019+, 2 vCPU, 4 GB |
| `WEB01` | SystemPanel web (IIS) + SQL Server (Express/Developer yeterli) | Windows Server 2019+, 2–4 vCPU, 8 GB |
| `SRV01` | Agent kurulacak hedef sunucu (IIS kurulu olursa IIS runbook örneği de denenir) | Windows Server 2016+, 2 vCPU, 4 GB |
| `NOC01` (isteğe bağlı) | Dashboard (WebView2) | Windows 10/11 veya Server |

Makine adları örnektir. Kendi adlarınızı kullanın ve paylaşırken gizleyin.

**Test AD yapısı.** Adlar örnektir ve `appsettings.Production.json` dosyasına yazılır, koda yazılmaz.

```
<domain kökü>
└── OU=IL                       (ActiveDirectory:Scope:AnchorOu)
    ├── OU=ANKARA
    │   ├── OU=Kullanicilar     (BranchOus)
    │   └── OU=Bilgisayarlar
    └── OU=IZMIR
        ├── OU=Kullanicilar
        └── OU=Bilgisayarlar
OU=Arsiv                        (offboard runbook'u için arşiv OU'su)
```

- **Gruplar:** her rol için bir grup (ör. `SP-Admin`, `SP-LocalAdmin-ANKARA`, `SP-LocalAdmin-IZMIR`, `SP-Operator`, `SP-Helpdesk`, `SP-Monitoring`). İl yönetici grup adlandırması `LocalAdminGroupSuffix` kuralına uymalıdır. Ayrıntı: [AUTHORIZATION.md](AUTHORIZATION.md).
- **Test kullanıcıları** (her biri tek bir rol grubunda):
  - `t.admin`, `t.ankara`, `t.izmir`, `t.operator`, `t.helpdesk`
  - İkinci bir onaylayıcı (dört göz testleri için): `t.ankara2`
  - Hedef kullanıcılar: Ankara ve İzmir'de birer kilitlenebilir kullanıcı ve bir `adminCount=1` (ayrıcalıklı) test hesabı
- **Web uygulama havuzu kimliği** (`IIS AppPool\SystemPanel`, ağda `WEB01$`):
  - Yalnızca `OU=IL` altında yetki devredilir (kullanıcı nesnelerinde: parola sıfırlama, `lockoutTime` yazma, etkinleştirme/devre dışı bırakma, taşıma).
  - `Domain Admins` **yapmayın**. Yetkinin yetersiz kaldığı durumlar da bu testin bulgusudur.
- LAPS testi için `SRV01`'de Windows LAPS etkin olmalı. Okuma yetkisi yalnızca test grubuna verilir.

## 2. Paketi edinme

Seçenek A, önerilen yol:
1. GitHub → **Actions** → `main` dalının son başarılı `.NET` çalıştırması → **Artifacts** → `systempanel-package`. Paket imzasız bir CI yapısıdır.
2. `SHA256SUMS.txt` dosyasıyla bütünlüğü doğrulayın: `Get-FileHash SystemPanel-*.zip`.

Seçenek B: Windows derleme makinesinde `pwsh ./build/Build-Release.ps1 -Version 0.9.0-test.1`.

CI paketinde yerleşik runbook'lar imzalı **değildir**; üretici anahtarı henüz üretilmedi. Otomasyon testleri (§H) bu yüzden
**test kurum anahtarıyla** yapılır. Bu yolla içe aktarılan kimlikler test anahtarına bağlanır (sahiplik kuralı), dolayısıyla
**test veritabanı test bitince atılır.**

## 3. Aşamalar ve test adımları

Her adım için sonucu §4'teki tabloya yazın: **G** (geçti), **K** (kaldı), **A** (atlandı) ve kısa not.

### A. Ön koşullar (kurulumdan önce)

| No | Adım | Beklenen |
|---|---|---|
| A1 | `WEB01`'de: `.\scripts\Test-SystemPanelPrerequisites.ps1 -Role Web -HostName <panel-adı> -CertificateThumbprint <parmak-izi> -SqlServer <sql> -LdapHost <dc> -OutputPath .\A1.json` | HATA yok. UYARI'ları not edin |
| A2 | `SRV01`'de: `... -Role Agent -HostName <panel-adı> -OutputPath .\A2.json` | Panel portu açık. Sertifika güveni A2 adımında **kurulumdan sonra** tekrar denenir |
| A3 | `NOC01`'de: `... -Role Dashboard -HostName <panel-adı>` | WebView2 kurulu |

### B. Kurulum ve ilk açılış

| No | Adım | Beklenen |
|---|---|---|
| B1 | SQL'de boş veritabanı oluşturun; `WEB01$` (veya aynı makinede `IIS AppPool\SystemPanel`) için login + `db_datareader`/`db_datawriter` + DDL yetkisi (yalnızca şema kurulumu sırasında) | |
| B2 | `database\migrate.sql` dosyasını SSMS ile uygulayın | Hatasız; `__EFMigrationsHistory` tablosunda 4+ kayıt |
| B3 | `.\scripts\Install-SystemPanel.ps1 -HostName <panel-adı> -CertificateThumbprint <...> -SqlServer <sql> -Database <db>` | `/health/live` kontrolü geçer; kurulum belirteci dosyasının yolu yazdırılır |
| B4 | `appsettings.Production.json` dosyasını doldurun: AD, LDAP, Scope, RoleMapping. Sırları `Protect-SystemPanelSecret.ps1` ile `dpapi:` değeri olarak verin. Uygulama havuzunu geri dönüştürün | Uygulama açılır |
| B5 | Kurulum öncesi duman testi: `.\scripts\Test-SystemPanelDeployment.ps1 -BaseUrl https://<panel-adı> -ExpectSetupOpen -OutputPath .\B5.json` | HATA yok |
| B6 | `https://<panel-adı>/setup`: belirteçle ilk (acil durum) yöneticiyi oluşturun | Belirteç dosyası silinir; `/setup` 404 döner |
| B7 | Duman testi (setup sonrası): `... -OutputPath .\B7.json` | HATA yok. S14 OK |
| B8 | Güncelleme provası: B3'ü aynı paketle tekrar çalıştırın | Yapılandırma ve loglar korunur, site çalışır |

### C. Kimlik, yetki ve kapsam

| No | Adım | Beklenen |
|---|---|---|
| C1 | `t.admin`, `t.ankara`, `t.operator`, `t.helpdesk` ile giriş | Her biri yalnızca rolünün menülerini görür |
| C2 | Hiçbir rol grubunda olmayan kullanıcıyla giriş | Giriş reddedilir |
| C3 | `t.ankara` ile İzmir OU'sundaki kullanıcıyı arayın ve detayını açın | Görünmez veya erişim reddedilir. Audit'te **red** kaydı |
| C4 | `t.ankara` ile İzmir kullanıcısının URL'sini elle açın | 403 / erişim reddi, audit kaydı |
| C5 | Admin → izin matrisi: Helpdesk'ten `ad.users.unlock` iznini kaldırın | Helpdesk kilit açamaz (403) |
| C6 | Oturum zaman aşımı ve çıkış | Süre dolunca giriş sayfasına yönlenir |

### D. Active Directory işlemleri (yalnızca test OU'ları)

| No | Adım | Beklenen |
|---|---|---|
| D1 | `t.ankara`: Ankara'da kullanıcı oluşturma | AD'de oluşur; audit kaydı |
| D2 | Parola sıfırlama | Başarılı. **Logda ve audit'te parola yok** (bkz. K2) |
| D3 | Kilitli test kullanıcısının kilidini açma | `LockedOut=false` |
| D4 | Etkinleştirme / devre dışı bırakma, OU taşıma (Ankara içinde) | Başarılı; audit |
| D5 | Ayrıcalıklı (`adminCount=1`) hesapta parola sıfırlama | Reddedilir |
| D6 | Grup üyeliği ekleme/çıkarma; `ProtectedGroups` içindeki gruba ekleme | Normal grup çalışır; korumalı grup reddedilir |
| D7 | `SRV01` için LAPS parolası gösterme | Bir kez gösterilir. Audit'te **olay var ama parola yok** |
| D8 | Uygulama havuzu kimliğinin yetkisi olmayan bir işlem (ör. `OU=IL` dışı) | Anlamlı hata mesajı; uygulama çökmez |

### E. GPO (isteğe bağlı)

| No | Adım | Beklenen |
|---|---|---|
| E1 | GPO listesi ve ayrıntı | GroupPolicy modülü ile listelenir |
| E2 | Test OU'suna bağlama/kaldırma | Başarılı; audit |
| E3 | Yedekleme | `Gpo:BackupRoot` altında yedek oluşur |

### F. Agent

| No | Adım | Beklenen |
|---|---|---|
| F1 | Panel → Agent'lar → `SRV01` kaydı (birim: ANKARA) | AgentId + anahtar **bir kez** gösterilir |
| F2 | `SRV01`'de: `.\Install-SystemPanelAgent.ps1 -ServerUrl https://<panel-adı> -AgentId <guid> -SourcePath ..\agent` | Servis çalışır (`NT SERVICE\SystemPanelAgent`) |
| F3 | Panelde heartbeat ve metrikler | Birkaç dakika içinde görünür |
| F4 | A2 tekrarı (sertifika güveni) | OK |
| F5 | Panel kesintisi: `WEB01`'de uygulama havuzunu 10 dk durdurun, sonra başlatın | Agent tamponladığı metrikleri gönderir; agent logunda geri çekilme görülür |
| F6 | Panelde **Anahtarı yenile**; eski anahtarla agent | 401. Kurulum betiğiyle yeni anahtar verince düzelir |
| F7 | Disk eşiği alarmı (eşiği düşük ayarlayın) | Alarm oluşur |

### G. NOC Dashboard (isteğe bağlı)

| No | Adım | Beklenen |
|---|---|---|
| G1 | `dashboard\` klasörünü çalıştırın; panel adresini girin | Paneller görünür |
| G2 | Panel sunucusunu durdurun | Son geçerli görünüm kalır; yeniden bağlanmayı dener |

### H. Otomasyon (runbook)

**Hazırlık:** Bir yönetici makinede:

```powershell
tools\sprunbook.exe keygen --out <güvenli-test-klasörü>
# Örnekleri ve yerleşik runbook'ları test anahtarıyla imzalayın (YALNIZCA TEST):
tools\sprunbook.exe sign-all --key <...>\runbook-org.private.pem --source runbook-examples --out paketler
```

Yerleşik runbook kaynakları sürüm paketinde yoktur, repodaki `runbooks/src` klasöründedir. Onları test etmek için o
klasörü kopyalayıp `sign-all --vendor` ile aynı test anahtarıyla imzalayın.

| No | Adım | Beklenen |
|---|---|---|
| H1 | Otomasyon → Katalog ve anahtarlar: test **açık** anahtarını kaydedin; parmak izini `sprunbook fingerprint` ile karşılaştırın | Eşleşir; audit `automation.key.add` |
| H2 | Paketleri içe aktarın | Katalogda "Kurum" etiketiyle görünür |
| H3 | Özel anahtar PEM'ini anahtar formuna yapıştırmayı deneyin | Reddedilir |
| H4 | `t.ankara`: `org.example.ad-group-report` (low) → Ankara OU'su | Onaysız çalışır; JSON sonuç |
| H5 | Aynı runbook, İzmir OU'su | Talep reddedilir (kapsam) |
| H6 | `org.example.ad-user-unlock` (medium): `t.ankara` talep eder | Önizleme kilit durumunu gösterir; `t.ankara` kendi talebini **onaylayamaz** |
| H7 | `t.ankara2` onaylar | Çalışır; kullanıcının kilidi açılır; audit zinciri tam |
| H8 | `ad.user.offboard` (high): önizleme → onay → çalıştırma (test kullanıcısı, `OU=Arsiv`) | Devre dışı + gruplardan çıkar + taşınır |
| H9 | Agent otomasyonu: `SRV01`'de `-AllowedRunbooks server.diagnostics.snapshot` ve `-TrustedRunbookKeyFiles <test açık anahtarı>` ile yeniden kurun | Agent işleri çekmeye başlar |
| H10 | `server.diagnostics.snapshot` → `SRV01` | Sonuç panelde görünür |
| H11 | İzin listesinde olmayan `server.service.restart` → `SRV01` | Agent **reddeder** (`RejectedByAgent`) |
| H12 | Zamanlama: H4 runbook'u için 5 dakikalık cron | Zamanında çalışır; tekrar tetiklemede üst üste binmez |
| H13 | Alarm tetikleyici: disk alarmı → `server.disk.temp-cleanup` (izin listesine ekleyin) | Low + agent olduğu için otomatik çalışır |
| H14 | Bekleyen bir medium talep ve etkin bir zamanlama varken test anahtarını **iptal edin** | Talep iptal, zamanlama kapalı; yeni çalıştırma yapılamaz; audit kayıtları |
| H15 | Uygulama havuzu `ActiveDirectory` modülüne erişemiyorsa (RSAT yok) bir AD runbook'u çalıştırın | Failed + anlaşılır hata, çökme yok |

### I. Lisans (isteğe bağlı)

CI yapısında lisans açık anahtarı gömülü değildir, bu yüzden lisans kısıtlaması yoktur. Lisansı denemek için test anahtarıyla
yapı üretmek gerekir (`splicense keygen` → `license-public.pem` → Build-Release). Bu, ayrı bir test turu olarak planlanabilir.

### J. Dayanıklılık ve işletim

| No | Adım | Beklenen |
|---|---|---|
| J1 | SQL servisini durdurun, `/health/live` ve `/health/ready` (admin) | `ready` db için Unhealthy; ekranlar anlamlı hata verir |
| J2 | SQL kapalıyken uygulama havuzunu geri dönüştürün | **Bilinen:** uygulama açılışta veritabanına bağlanamazsa başlamaz (IIS 500.30). SQL açılınca geri dönüştürme ile düzelir. Bulguyu not edin |
| J3 | Sunucuyu yeniden başlatın | Site ve agent kendiliğinden açılır; yarıda kalan runbook işi `Failed` olarak işaretlenir |
| J4 | `Uninstall-SystemPanel.ps1` | Site kalkar; veritabanı ve `%ProgramData%\SystemPanel` korunur |

### K. Güvenlik gözlemleri

| No | Adım | Beklenen |
|---|---|---|
| K1 | Tarayıcı geliştirici araçları → Ağ: harici adrese istek | Hiç yok (kapalı ağ) |
| K2 | Test boyunca kullanılan test parolalarını loglarda arayın: `Select-String -Path "$env:ProgramData\SystemPanel\logs\*" -Pattern '<test parolası>'`. Aynı aramayı SQL'de `AuditEvents` tablosunda da yapın | **Hiç sonuç yok** |
| K3 | `appsettings.Production.json` içinde açık metin sır | Yok (yalnızca `dpapi:` değerleri) |
| K4 | `%ProgramData%\SystemPanel` ve agent ayar dosyası ACL'leri | Yalnızca Administrators, SYSTEM ve servis hesapları |

## 4. Sonuç bildirimi

Aşağıdaki tabloyu doldurup gönderin. Ekleyin:
- **Rapor dosyaları:** `A1.json`, `A2.json`, `B5.json`, `B7.json`. Bunlar adları zaten gizler.
- **Kalan adımlar için:** hata mesajı ve ilgili log satırları (`%ProgramData%\SystemPanel\logs`, Olay Görüntüleyicisi → Application, agent logu). Göndermeden önce sunucu, domain ve kişi adlarını `<sunucu>`, `<domain>`, `<kullanıcı>` ile değiştirin.
- **Ekran görüntüleri:** yalnızca gerekirse ve adlar karartılmış olarak.

**Göndermeyin:** parola, anahtar dosyası (`*.pem`, `*.lic`), bağlantı dizesi, `appsettings.Production.json`'un kendisi,
kurulum belirteci, agent anahtarı.

| No | Sonuç (G/K/A) | Not / hata mesajı (adlar gizli) |
|---|---|---|
| A1 | | |
| A2 | | |
| ... | | |

## 5. Sorun giderme

| Belirti | Olası neden |
|---|---|
| HTTP 500.30 / site açılmıyor | Veritabanına ulaşılamıyor, SQL login yetkisi yok veya Hosting Bundle eksik. `%ProgramData%\SystemPanel\logs` ve Olay Görüntüleyicisi'ne bakın |
| HTTP 500.19 | ASP.NET Core Module yok. Hosting Bundle kurun, ardından `iisreset` |
| AD kullanıcıları giriş yapamıyor | `RoleMapping` grup adları eşleşmiyor ya da kullanıcı hiçbir role eşlenmiyor (bkz. C2) |
| Kapsam beklenenden dar veya geniş | `ActiveDirectory:Scope` (`AnchorOu`, `BranchOus`, `LocalAdminGroupSuffix`) değerleri |
| Agent 401 | Anahtar hatalı veya yenilenmiş, agent devre dışı ya da saat farkı |
| Agent bağlanamıyor (TLS) | `SRV01` kurum CA'sına güvenmiyor (A2/F4) |
| Runbook "anahtar güvenilir değil" | Anahtar kaydedilmemiş veya iptal edilmiş; agent tarafında `TrustedKeyFiles` eksik |
| Duman testinde HSTS UYARI | `localhost` ile test edildi (HSTS localhost'a gönderilmez) veya ara cihaz başlığı siliyor |
