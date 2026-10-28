# SystemPanel — Kurulum ve Dağıtım

Hedef: kapalı ağda Windows Server, IIS (InProcess) veya Windows servisi, SQL Server, Active Directory.

## 1. Gereksinimler

- Windows Server 2019+, IIS (`Install-WindowsFeature Web-Server -IncludeManagementTools`)
- **.NET 9 Hosting Bundle** (IIS için ASP.NET Core Module). Uygulama self-contained olduğu için ayrı .NET runtime gerekmez. Kapalı ağda çevrimdışı kurulum dosyasını kullanın.
- SQL Server 2017+
- AD'ye LDAPS (636) erişimi önerilir
- GPO modülü için: RSAT Group Policy Management (`GroupPolicy` PowerShell modülü) ve yetkili hizmet hesabı
- Otomasyon için: `RSAT-AD-PowerShell` ve uygulama havuzu kimliğine gereken OU'larda devredilmiş yetki (bkz. [AUTOMATION.md](AUTOMATION.md))
- HTTPS sertifikası (kurum CA'sı), sunucunun `LocalMachine\My` deposunda

## 2. Hızlı kurulum (önerilen)

Sürüm paketi `SystemPanel-<sürüm>.zip` olarak gelir. Önce `SHA256SUMS.txt` ile bütünlüğünü doğrulayın, sonra açın:

```
web\  agent\  dashboard\  database\ (migrate.sql, efbundle.exe)  scripts\  docs\
```

Yönetici PowerShell'de:

```powershell
cd <paket>\scripts
.\Install-SystemPanel.ps1 -HostName panel.kurum.local -CertificateThumbprint <parmak-izi> -SqlServer <sql-sunucu> -Database SystemPanel
```

Betik şunları yapar:
- Ön koşulları kontrol eder.
- IIS uygulama havuzunu ve HTTPS sitesini oluşturur.
- Dosyaları kopyalar.
- `%ProgramData%\SystemPanel` izinlerini ayarlar.
- Sır içermeyen `appsettings.Production.json` dosyasını oluşturur (SQL için Windows Authentication).
- Siteyi başlatıp `/health/live` adresini kontrol eder.

**Güncelleme** için aynı komutu yeni paketle çalıştırın. Mevcut yapılandırma ve loglar korunur.

**Veritabanı:** Betik varsayılan olarak şemaya dokunmaz. İki seçenek var:
- DBA, `database\migrate.sql` dosyasını uygular (idempotent; önce yedek alınır).
- Betik `-ApplyDatabaseMigrations` ile çalıştırılır. Onay istenir ve şema, betiği çalıştıran hesabın yetkisiyle güncellenir.

**SQL erişimi:** SQL ayrı sunucudaysa web sunucusunun bilgisayar hesabına (`DOMAIN\SUNUCU$`), aynı sunucudaysa `IIS AppPool\SystemPanel` hesabına login ve `db_datareader` / `db_datawriter` yetkisi verin.

**İlk yönetici:** `https://<sunucu>/setup` adresini açın. İstenen kurulum belirteci, sunucuda yalnızca yöneticilerin okuyabildiği `%ProgramData%\SystemPanel\setup-token.txt` dosyasındadır. Kurulumdan sonra belirteç silinir ve sayfa kapanır. Bu hesap acil durum (break-glass) hesabıdır; günlük kullanımda AD hesapları kullanılır.

**Kaldırma:** `.\Uninstall-SystemPanel.ps1`. Veritabanı ve `%ProgramData%\SystemPanel` verileri korunur.

## 2a. Paketin üretilmesi (üretici)

```powershell
pwsh ./build/Build-Release.ps1 -Version 1.0.0 -SigningCertThumbprint <kod-imzalama-sertifikası> -TimestampUrl <zaman-damgası-url>
```

- Paket `artifacts\` altında oluşur.
- CI her push'ta paketin üretilebildiğini doğrular (imzasız).
- Müşteriye giden paket, kod imzalama sertifikası olan derleme makinesinde imzalı olarak üretilir.
- **Çevrimdışı NuGet:** Kapalı ağdaki derleme makinesinde `nuget.offline.example.config` dosyasını `nuget.config` olarak kopyalayın.

## 3. Yapılandırma

1. `appsettings.Production.example.json` → sunucuda `appsettings.Production.json` (git'e eklemeyin). `<...>` değerlerini doldurun.
2. Sırları **boş bırakın** ve şunlardan biriyle verin:
   - DPAPI: `.\scripts\Protect-SystemPanelSecret.ps1` → `dpapi:...` değerini ilgili anahtara yazın.
   - Ortam değişkeni (IIS uygulama havuzu / servis): `Ldap__BindPassword`, `ConnectionStrings__DefaultConnection`, ...
3. SQL için mümkünse Windows Authentication (`Integrated Security=True`) ve uygulama havuzu kimliği kullanın.
4. `RoleMapping` ve `ActiveDirectory:Scope` → bkz. [AUTHORIZATION.md](AUTHORIZATION.md).
5. İlk yönetici: `/setup` sihirbazı (bkz. §2). Alternatif (otomatik kurulumlar): `Bootstrap__AdminUserName`, `Bootstrap__AdminPassword` ortam değişkenleri. Parola en az 12 karakter, büyük/küçük harf ve rakam içermeli. Hesap oluştuktan sonra **bu değişkenleri kaldırın.**
6. Organizasyon yapısı: `Organization:UnitLabel` / `UnitLabelPlural` (ör. "İl"/"İller", "Bölge"/"Bölgeler"; varsayılan "Birim"). Birimleri Türkiye illerine karşılık geliyorsa `Organization:Map:Enabled=true` ile il haritası modülü açılır (varsayılan kapalı).
7. Dosya yolları: `FileStorage:Root`, `Gpo:BackupRoot`, `Security:DataProtectionKeysPath` (boşsa `%ProgramData%\SystemPanel\...`).

## 3a. Lisans

Ürün yapısında **Lisans** ekranından `.lic` dosyasını yükleyin veya `%ProgramData%\SystemPanel\license.lic` olarak kopyalayın. Ayrıntı: [LICENSING.md](LICENSING.md).

## 4. HTTPS

`Security:RequireHttps=true` varsayılandır: HTTP istekleri HTTPS'e yönlendirilir, HSTS gönderilir, cookie'ler `Secure` olur.

- IIS'te HTTPS binding + sertifika ekleyin.
- Geçiş döneminde HTTP zorunluysa `Security__RequireHttps=false` (açılışta uyarı loglanır). Bu ayar geçici olmalıdır.
- Agent ve Dashboard'lara verilen sunucu adresi `https://` olmalıdır.

## 5. Veritabanı

**Doğrudan `database update` çalıştırmayın.** Önce yedek alın ve idempotent script üretip gözden geçirin. Ayrıntı: [DATABASE.md](DATABASE.md).

## 6. Doğrulama

Kurulumdan önce `scripts\Test-SystemPanelPrerequisites.ps1`, sonra `scripts\Test-SystemPanelDeployment.ps1` çalıştırın. İkisi de salt okunurdur. İlk gerçek ortam denemesi için adım adım plan: [FIELD-VALIDATION.md](FIELD-VALIDATION.md).

```text
GET  /health/live          → 200 {"status":"Healthy"}          (anonim)
GET  /health/ready         → settings.view ile: db / ldap / filesystem
GET  /Account/Login        → yanıt başlıklarında CSP, X-Content-Type-Options
```

Ardından: test kullanıcılarıyla giriş yapın, bir agent kaydedin ve heartbeat'in geldiğini görün, Denetim Kayıtları ekranını kontrol edin.

## 7. Dashboard (NOC ekranı)

- `out/dashboard` klasörünü kopyalayın. WebView2 Runtime gereklidir.
- Toplu dağıtımda `%ProgramData%\SystemPanel\Dashboard\appsettings.json` dosyasını önceden yerleştirin (örnek: `src/SystemPanel.Dashboard/appsettings.example.json`). Dosya geçerliyse kurulum penceresi açılmaz.
- Loglar: `%ProgramData%\SystemPanel\Dashboard\logs` (14 gün). Son geçerli yapılandırma `config-cache.json` dosyasında tutulur; sunucu kapalıyken ekran son panelleri gösterir ve geri çekilerek yeniden dener.
