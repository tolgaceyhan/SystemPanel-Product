# SystemPanel — Güvenlik

Kurulumda değiştirilmesi/oluşturulması gereken kimlik bilgileri: SQL bağlantı hesabı (tercihen Windows Authentication), en az yetkili LDAP bind hesabı (yetkiler birim OU'larına devredilmiş), SCCM SQL/WMI hesapları, ilk yönetici (yalnızca kurulumda). Hiçbiri dosyaya düz metin yazılmaz (bkz. §1).

## 1. Sır yönetimi

- Kaynak kontrolündeki `appsettings.json` sır ve altyapıya özgü değer içermez. Ortam değerleri: `appsettings.Production.json` (git dışı; şablon `appsettings.Production.example.json`), ortam değişkenleri (`ConnectionStrings__DefaultConnection` vb.).
- **DPAPI korumalı değerler:** Herhangi bir yapılandırma değeri `dpapi:<base64>` biçiminde verilebilir. Uygulama açılışta bu değeri Windows DPAPI (LocalMachine, entropy `SystemPanel.Configuration.v1`) ile çözer. Değer üretmek için sunucuda:

  ```powershell
  .\scripts\Protect-SystemPanelSecret.ps1   # parolayı gizli olarak sorar, dpapi:... yazar
  ```

  DPAPI LocalMachine değerleri yalnızca üretildiği makinede çözülebilir; her sunucu için ayrı üretin. Dosya ACL'si: yalnızca Administrators + uygulama havuzu kimliği okuyabilir.
- Agent anahtarı da aynı şekilde (`dpapi:` öneki, entropy `SystemPanel.Agent.Key.v1`) saklanır; kurulum betiği bunu otomatik yapar.
- Yasaklar: sırların loglanması, audit'e yazılması, URL'de taşınması, e-posta/SMS ile gönderilmesi.

## 2. Uygulama güvenlik kontrolleri

| Kontrol | Ayrıntı |
|---|---|
| HTTPS | `Security:RequireHttps=true` (varsayılan): HTTPS yönlendirme, HSTS (180 gün), `Secure` cookie. Geçiş dönemi için `false` yapılabilir; açılışta uyarı loglanır. |
| Oturum | Cookie `SystemPanel.Auth`, HttpOnly, SameSite=Lax, kayan süre `Security:SessionTimeoutMinutes`. |
| Lockout | Yerel hesaplar `Security:MaxFailedAccessAttempts` / `LockoutMinutes`. AD hesapları için AD politikası geçerlidir. |
| DataProtection | Anahtarlar `%ProgramData%\SystemPanel\DataProtection-Keys` (veya `Security:DataProtectionKeysPath`), Windows'ta DPAPI ile şifreli. Web kökü dışında. |
| CSRF | Tüm durum değiştiren POST'larda antiforgery. |
| Başlıklar | CSP (frame-ancestors, object-src, base-uri, form-action), nosniff, `X-Frame-Options: SAMEORIGIN`, Referrer-Policy, COOP, Permissions-Policy. Script kaynaklarını kısıtlayan tam CSP ROADMAP'tedir (satır içi script'ler temizlenmeli). |
| Rate limit | Agent uçları: IP başına `Monitoring:RateLimitPerMinute`. |
| Dosya yükleme | Uzantı allowlist'i, boyut sınırı (`FileStorage:MaxFileBytes`), yol normalizasyonu (path traversal engeli), web kökü dışında depolama. |
| PowerShell | Yalnızca `src/SystemPanel/scripts/*.ps1`; `-File` + `ArgumentList`; GPO adı/GUID/OU DN doğrulaması; `Gpo:AllowDelete`/`AllowImport` varsayılan kapalı. |
| LAPS | Listelerde okunmaz. Tek bilgisayar için, `laps.read` izni + kapsam kontrolü + audit ile "göster" butonu. Yanıt `no-store`. Parola log/audit'e yazılmaz. |
| Ayrıcalıklı hesaplar | `adminCount=1` hesaplar ve korumalı gruplar (`Domain Admins` vb. + `ActiveDirectory:ProtectedGroups`) yalnızca `scope.all` sahibi kullanıcılarca değiştirilebilir. |
| LDAP enjeksiyonu | Tüm filtre değerleri `LdapFilter.Escape` ile kaçırılır. |

## 3. Audit

`AuditEvents` tablosu: zaman (UTC), kullanıcı, eylem (`AuditActions`), kaynak türü/kimliği, sonuç (Success/Failure/Denied), hata, önce/sonra (JSON, maskelenmiş), IP, user agent, correlation id. Uygulamadan silinemez. Görüntüleme: **Denetim Kayıtları** ekranı (`audit.view`). Saklama süresi ve SIEM'e aktarım ROADMAP'tedir.

## 4. Bilinen açık riskler

- Dashboard yapılandırma ucu anonimdir (yalnızca panel başlık/URL bilgisi döner).
- Eski anonim ingest ucu `Monitoring:AllowLegacyAnonymousIngest=true` ile geçici olarak açılabilir. Açıkken herkes sahte metrik gönderebilir; yalnızca agent geçişi sırasında ve kısa süre kullanın.
- `Security:RequireHttps=false` iken oturum cookie'leri ağda okunabilir.

## 5. Güvenlik açığı bildirimi

Güvenlik açıklarını üreticiye doğrudan ve gizli olarak bildirin; issue tracker'a sır veya sömürü ayrıntısı yazmayın.
