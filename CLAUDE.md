# CLAUDE.md — SystemPanel

Kapalı ağ Windows altyapı yönetim platformu. Belgeler Türkçe; kod tanımlayıcıları İngilizce, yorumlar Türkçe.

## Komutlar

```bash
dotnet build SystemPanel.sln
DOTNET_ROLL_FORWARD=Major dotnet test SystemPanel.sln      # SDK 10 + net9.0 hedefi için roll-forward
DOTNET_ROLL_FORWARD=Major dotnet ef migrations add <Ad> --project src/SystemPanel
```

- `dotnet ef migrations remove` bu repoda çalışmaz (bağlantı gerektirir). Migration dosyalarını elle silin ve snapshot'ı `git checkout` ile geri alın.
- Dashboard WPF'tir; Linux'ta yalnızca derlenir (`EnableWindowsTargeting`). WPF'siz çekirdek `Core/` altındadır ve `tests/SystemPanel.Dashboard.Tests` ile test edilir.

## Yerleşim

- `src/SystemPanel/Security/`: `Permissions` (izin kataloğu), `DefaultRolePermissions`, `PermissionService`, `RequirePermissionAttribute`, `Scope/` (`DistinguishedName`, `OrganizationalScope`, `ScopeDerivation`)
- `src/SystemPanel/Auditing/`: `IAuditService`, `AuditActions`, `AuditRedactor`
- `src/SystemPanel/Services/ActiveDirectory/`: `IAdUserOperations`, `AdComputerOperations`, `LapsReader` — **AD üzerinde değişiklik yapan her işlem buradan geçer**
- `src/SystemPanel/Services/Agents/`: agent kimliği, ingest, retention
- `src/SystemPanel/Services/Gpo/` + `src/SystemPanel/scripts/*.ps1`: GPO (yalnızca sabit betikler)
- `src/SystemPanel.Contracts/`: agent API sözleşmesi ve lisans biçimi (`Licensing/`)
- `src/SystemPanel/Licensing/`: `ILicenseService`; `tools/SystemPanel.LicenseTool`: üretici aracı (lisans + runbook imzalama)
- `src/SystemPanel/Automation/`: otomasyon motoru (ADR-002): `RunbookCatalog`, `RunbookTrustStore`, `RunbookParameterBinder`, `RunbookRunService` (TÜM durum geçişleri burada), `PowerShellRunbookScriptHost`
- `runbooks/src/<id>/`: yerleşik runbook kaynakları (`manifest.json` + `run.ps1`); sürüm paketinde imzalanır

## Kurallar (zorunlu)

1. **Sır yok:** Parola, anahtar veya bağlantı dizesi kod, yapılandırma, test, log ve belgeye yazılmaz. Müşteri ortamlarından alınan hiçbir değer (log, yapılandırma, ekran görüntüsü) repoya girmez.
2. **Altyapı değeri yok:** IP, domain, OU, AD grup adı, SCCM sunucusu/site kodu ve mutlak yol kodda yer almaz. Bunlar `*Options` sınıflarından gelir; örnekler `<...>` veya `.example.invalid` kullanır.
3. **Yetki:** Her yeni controller/aksiyon `[RequirePermission(Permissions.X)]` taşır. Anonim uç yalnızca açık gerekçeyle `[AllowAnonymous]` olur. Rol adı controller'da kullanılmaz.
4. **Kapsam:** AD hedefi alan her işlem `IOrganizationalScopeResolver` ile kapsam kontrolü yapar. DN karşılaştırması `DistinguishedName` ile yapılır; `EndsWith` kullanılmaz. Kapsam belirsizse erişim reddedilir.
5. **Audit:** Yıkıcı veya ayrıcalıklı her işlem `IAuditService.RecordAsync` ile kaydedilir (başarı, hata ve red). LAPS/parola değerleri asla loglanmaz veya audit'e yazılmaz.
6. **Komut çalıştırma yok:** `-Command` + string birleştirme yasak. PowerShell yalnızca `PowerShellScriptRunner` (sabit betikler) veya `PowerShellRunbookScriptHost` (imzası doğrulanmış runbook) ile, `-File` + doğrulanmış parametreyle çağrılır. Keyfi uzak komut / serbest betik özelliği eklenmez. Runbook değişikliği = yeni sürüm + yeni imza.
7. **CSRF:** Durum değiştiren her POST'ta `[ValidateAntiForgeryToken]` bulunur. GET yan etki üretmez.
8. **LDAP:** Filtre değerleri `LdapFilter.Escape` ile kaçırılır.
9. **Migration:** Yalnızca ekleyici migration'lar yazılır. Üretim veritabanına migration uygulamak, kimlik bilgisi rotasyonu, gerçek AD/GPO yıkıcı işlemleri ve git geçmişi yeniden yazımı **kullanıcı onayı olmadan yapılmaz.**
10. **Bağımlılık lisansı:** Yalnızca ticari dağıtıma uygun lisanslar (MIT, Apache-2.0, BSD, MS-PL). AGPL/GPL/SSPL yasak. Yeni paket `THIRD-PARTY-NOTICES.md`'ye eklenir.
11. **Lisans:** Lisans kontrolü hiçbir zaman mevcut işlemleri, oturum açmayı veya izlemeyi durdurmaz; yalnızca yeni kapasiteyi sınırlar. İmzalama özel anahtarı ve `.lic` dosyaları repoya girmez.
12. Küçük ve gözden geçirilebilir commit'ler yazın. Büyük yeniden yazımlardan kaçının. Çalışan modülleri kusurlu diye silmeyin.

## Testler

- `tests/SystemPanel.UnitTests`: güvenlik, kapsam, audit, GPO girdisi, dosya yolu
- `tests/SystemPanel.IntegrationTests`: `SystemPanelFactory` (SQLite in-memory, `X-Test-Roles` başlığıyla test kimliği; başlık yoksa anonim)
- `tests/AgentSystemPanel.Tests`, `tests/SystemPanel.Dashboard.Tests`

Yeni bir uç eklendiğinde en az şu testler yazılır: anonim → 401/302 ve izinsiz → 403.
