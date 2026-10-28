# SystemPanel

Kapalı ağ (on-prem) Windows altyapısı için yönetim platformu:
- Active Directory kullanıcı/bilgisayar işlemleri (il bazlı kapsam)
- LAPS, GPO ve SCCM işlemleri
- Agent tabanlı sunucu izleme ve alarm
- NOC dashboard ekranları
- Duyuru, SMS ve dosya modülleri

| Bileşen | Konum |
|---|---|
| Web uygulaması (ASP.NET Core MVC, .NET 9) | `src/SystemPanel` |
| Agent ↔ sunucu sözleşmeleri | `src/SystemPanel.Contracts` |
| İzleme agent'ı (Windows servisi) | `src/AgentSystemPanel` |
| NOC Dashboard (WPF + WebView2) | `src/SystemPanel.Dashboard` |
| Lisans aracı (yalnızca üretici) | `tools/SystemPanel.LicenseTool` |
| Testler | `tests/` |

## Hızlı başlangıç (geliştirme)

```bash
dotnet restore SystemPanel.sln
dotnet build SystemPanel.sln
dotnet test SystemPanel.sln
```

1. `src/SystemPanel/appsettings.Development.example.json` → `appsettings.Development.json` (git dışı) ve yerel değerleri girin.
2. Sırları dosyaya yazmayın. Ortam değişkeni kullanın (`ConnectionStrings__DefaultConnection`, `Ldap__BindPassword`, `Bootstrap__AdminPassword` ...).
3. Yerel HTTP ile çalışacaksanız: `Security__RequireHttps=false`.
4. Veritabanı: `dotnet ef database update --project src/SystemPanel` (yalnızca geliştirme veritabanında).

## Belgeler

- [Mimari](docs/ARCHITECTURE.md) · [Güvenlik](docs/SECURITY.md) · [Kurulum](docs/DEPLOYMENT.md)
- [Yetkilendirme](docs/AUTHORIZATION.md) · [Agent](docs/AGENT.md) · [Veritabanı](docs/DATABASE.md)
- [Yol haritası](docs/ROADMAP.md) · [ADR-001 İzleme](docs/ADR/001-monitoring-architecture.md) · [ADR-002 Otomasyon](docs/ADR/002-automation-runbooks.md)
- [Otomasyon](docs/AUTOMATION.md) · [Runbook geliştirme](docs/RUNBOOK-DEVELOPMENT.md) · [Ürün lisanslama](docs/LICENSING.md) · [Üçüncü taraf lisansları](THIRD-PARTY-NOTICES.md)
- [Değişiklik günlüğü](CHANGELOG.md)

## Güvenlik

Repoya parola, anahtar, bağlantı dizesi, IP, domain, OU veya grup adı eklemeyin. Ayrıntılar: [docs/SECURITY.md](docs/SECURITY.md).
