# SystemPanel — Kurulum ve Dağıtım

Hedef: kapalı ağda Windows Server, IIS (InProcess) veya Windows servisi, SQL Server, Active Directory.

## 1. Gereksinimler

- Windows Server 2019+; .NET 9 ASP.NET Core Hosting Bundle (veya self-contained yayın)
- SQL Server 2017+
- AD'ye LDAPS (636) erişimi önerilir
- GPO modülü için: RSAT Group Policy Management (`GroupPolicy` PowerShell modülü) ve yetkili hizmet hesabı
- HTTPS sertifikası (kurum CA'sı)

## 2. Derleme

```bash
dotnet restore SystemPanel.sln
dotnet build SystemPanel.sln -c Release
dotnet test SystemPanel.sln
dotnet publish src/SystemPanel -c Release -o out/web          # win-x64, self-contained (Release)
dotnet publish src/AgentSystemPanel -c Release -o out/agent
dotnet publish src/SystemPanel.Dashboard -c Release -o out/dashboard
```

**Çevrimdışı NuGet:** Kapalı ağdaki derleme makinesinde `nuget.offline.example.config` dosyasını `nuget.config` olarak kopyalayın ve yerel paket klasörünü gösterin. Repodaki `nuget.config` nuget.org'u kullanır (CI için).

SDK 10 ile `net9.0` hedefini derlerken test/ef araçları için `DOTNET_ROLL_FORWARD=Major` gerekebilir.

## 3. Yapılandırma

1. `appsettings.Production.example.json` → sunucuda `appsettings.Production.json` (git'e eklemeyin). `<...>` değerlerini doldurun.
2. Sırları **boş bırakın** ve şunlardan biriyle verin:
   - DPAPI: `.\scripts\Protect-SystemPanelSecret.ps1` → `dpapi:...` değerini ilgili anahtara yazın.
   - Ortam değişkeni (IIS uygulama havuzu / servis): `Ldap__BindPassword`, `ConnectionStrings__DefaultConnection`, ...
3. SQL için mümkünse Windows Authentication (`Integrated Security=True`) ve uygulama havuzu kimliği kullanın.
4. `RoleMapping` ve `ActiveDirectory:Scope` → bkz. [AUTHORIZATION.md](AUTHORIZATION.md).
5. İlk kurulum yöneticisi (opsiyonel): `Bootstrap__AdminUserName`, `Bootstrap__AdminPassword` ortam değişkenleri. Parola en az 12 karakter, büyük/küçük harf ve rakam içermeli. Hesap oluştuktan sonra **bu değişkenleri kaldırın.**
6. Dosya yolları: `FileStorage:Root`, `Gpo:BackupRoot`, `Security:DataProtectionKeysPath` (boşsa `%ProgramData%\SystemPanel\...`).

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
