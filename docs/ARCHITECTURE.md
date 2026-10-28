# SystemPanel — Mimari

Kapalı ağda (on-prem) çalışan Windows altyapı yönetim platformu: Active Directory, LAPS, GPO, SCCM, sunucu izleme ve NOC ekranları.

## Bileşenler

```
                         ┌──────────────────────────── Kurum ağı (kapalı) ────────────────────────────┐
                         │                                                                             │
 Tarayıcı (yönetici) ───►│  IIS / Kestrel (HTTPS)                                                      │
                         │  ┌───────────────────────── SystemPanel (ASP.NET Core MVC, net9.0) ───────┐ │
 NOC ekranı              │  │ Middleware: CorrelationId → ExceptionHandler → HTTPS/HSTS →            │ │
 SystemPanel.Dashboard ─►│  │             SecurityHeaders → Serilog → Auth (Cookie | Agent) →        │ │
 (WPF+WebView2)          │  │             Authorization (FallbackPolicy + perm:*) → RateLimiter      │ │
   GET /api/v1/dashboard │  │                                                                        │ │
                         │  │ Controllers (MVC + /api/v1)  ──►  Uygulama servisleri                  │ │
 Windows Server          │  │   [RequirePermission]              IAdUserOperations (kapsam + audit)  │ │
 SystemPanel Agent ─────►│  │                                    AdComputerOperations / LapsReader   │ │
   POST /api/v1/agents/* │  │                                    GpoService → PowerShellScriptRunner │ │
   X-Agent-Id/X-Agent-Key│  │                                    MetricsIngestionService, AgentReg.  │ │
                         │  │                                    IAuditService (ayrı DbContext scope)│ │
                         │  └──────────┬───────────────┬──────────────────┬───────────────┬─────────┘ │
                         │             │               │                  │               │           │
                         │        SQL Server     AD (LDAP/LDAPS)   PowerShell (GPO   SCCM SQL / WMI   │
                         │   (EF Core 9; Identity,  System.Directory  modülü, -File,    (allowlist'li   │
                         │    AuditEvents, Agents,  Services          ArgumentList)     servis kontrol) │
                         │    metrik tabloları)                                                        │
                         └─────────────────────────────────────────────────────────────────────────────┘
```

| Proje | Tür | Açıklama |
|---|---|---|
| `src/SystemPanel` | ASP.NET Core MVC (`net9.0`) | Web arayüzü + API. IIS InProcess veya Windows servisi olarak çalışır. |
| `src/SystemPanel.Contracts` | Class library | Agent ↔ sunucu sözleşmeleri (`AgentApi`, istek/yanıt DTO'ları). |
| `src/AgentSystemPanel` | Worker Service (`SystemPanel.Agent.exe`) | Sunuculara kurulan izleme agent'ı. Bkz. [AGENT.md](AGENT.md). |
| `src/SystemPanel.Dashboard` | WPF + WebView2 (`net9.0-windows`) | NOC ekranı: sunucudan panel yapılandırmasını okur, 1–4 web paneli gösterir. |
| `tests/*` | xUnit | Birim, entegrasyon (WebApplicationFactory + SQLite), agent ve dashboard çekirdek testleri. |

## Temel ilkeler

1. **Varsayılan reddet.** `FallbackPolicy` kimliği doğrulanmış kullanıcı ister; anonim uçlar `[AllowAnonymous]` ile açıkça işaretlidir (login, hata sayfası, `/health/live`, dashboard yapılandırması, kapatılmış eski ingest).
2. **İzin tabanlı yetki.** Controller'lar rol değil izin ister (`[RequirePermission(Permissions.X)]`). Rol → izin eşlemesi veritabanındadır (`RolePermissions`). Bkz. [AUTHORIZATION.md](AUTHORIZATION.md).
3. **Organizasyonel kapsam servis katmanında.** AD işlemleri `IAdUserOperations` / `AdComputerOperations` üzerinden yapılır. Hedef nesnenin DN'i kullanıcının kapsam köklerine (OU) göre RFC 4514 ayrıştırması ile doğrulanır. Kapsam türetilemezse erişim yoktur (fail-closed).
4. **Her yıkıcı/ayrıcalıklı işlem audit'lenir.** `AuditEvents` tablosu; kim, ne, hangi hedef, sonuç, correlation id, IP. Parola/anahtar alanları `AuditRedactor` ile maskelenir. LAPS parolası hiçbir zaman loglanmaz.
5. **Altyapıya özgü değer kodda yok.** Domain, OU, grup, sunucu, IP, yol değerleri yapılandırmadan gelir. Sırlar repoda tutulmaz (DPAPI / ortam değişkeni). Bkz. [SECURITY.md](SECURITY.md).
6. **Keyfi komut çalıştırma yok.** PowerShell yalnızca repodaki sabit betiklerle, `-File` + parametre listesi ve doğrulanmış girdiyle çağrılır. SCCM servis kontrolü allowlist ile sınırlıdır.

## İzleme akışı (ADR-001)

Agent-öncelikli push modeli: [ADR/001-monitoring-architecture.md](ADR/001-monitoring-architecture.md).

```
Agent ──HTTPS──► POST /api/v1/agents/heartbeat   (Agents.LastSeenUtc, sürüm, OS)
      └────────► POST /api/v1/agents/metrics     → MetricsIngestionService
                                                   ├─ LatestServerMetrics (son değer)
                                                   ├─ ServerMetricHistory / ServerDiskHistory
                                                   └─ AlarmLogs (eşik + cooldown)
MetricsRetentionService (opsiyonel) → eski geçmiş/alarm kayıtlarını toplu siler
AgentStatusService → AgentOfflineAfterSeconds'a göre çevrimiçi/çevrimdışı
```

## Kimlik doğrulama

- **Kullanıcı:** ASP.NET Identity cookie (`SystemPanel.Auth`). Giriş `LoginService` ile AD'ye karşı (UserPrincipal + grup üyelikleri → `RoleMapping`) yapılır; eşleşen rol yoksa giriş reddedilir. Yerel Identity hesapları (bootstrap admin) lockout ile korunur.
- **Agent:** `Agent` şeması. `X-Agent-Id` + `X-Agent-Key` başlıkları. Sunucu yalnızca anahtarın SHA-256 özetini saklar; karşılaştırma sabit zamanlıdır. IP başına rate limit uygulanır.
- **Dashboard:** Yapılandırma ucu anonimdir (kiosk makinelerinde kullanıcı oturumu yok). Yanıt yalnızca aktif panellerin başlık/URL/konum bilgisidir; URL'ler sunucuda http/https allowlist'i ile filtrelenir. Kimlik doğrulamalı dashboard ROADMAP'tedir.

## Çapraz kesen konular

| Konu | Uygulama |
|---|---|
| Correlation ID | `CorrelationIdMiddleware`: `X-Correlation-ID` (geçerliyse gelen değer, değilse yeni), Serilog `LogContext`, audit kayıtları, hata sayfası. |
| Hata modeli | MVC: dostça hata sayfası + correlation id. `/api`: `ProblemDetails`. Durum kodu sayfaları yalnızca `/api` dışı yollar için. |
| Güvenlik başlıkları | `SecurityHeadersMiddleware`: CSP (frame-ancestors/object-src/base-uri/form-action), `X-Content-Type-Options`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy`, `Cross-Origin-Opener-Policy`, `Permissions-Policy`. |
| CSRF | Tüm POST'larda `[ValidateAntiForgeryToken]`. AJAX için `wwwroot/js/csrf.js` meta etiketindeki belirteci `RequestVerificationToken` başlığına ekler. |
| Health | `/health/live` anonim (yalnızca durum). `/health/ready` `settings.view` ister (DB, LDAP, dosya sistemi). |
| Arka plan işleri | `BackgroundJobs:<Ad>:Enabled` ile açılıp kapatılır. Testlerde kapalıdır. |

## Dizin yerleşimi

```
src/SystemPanel/
  Auditing/          AuditService, AuditRedactor, AuditActions
  Configuration/     *Options, DpapiConfiguration
  Controllers/       MVC + Api/ (v1 agent uçları)
  Infrastructure/    CorrelationId, SecurityHeaders
  Security/          Permissions, PermissionService, policy provider, Scope/ (DN, kapsam)
  Services/          ActiveDirectory/, Agents/, Gpo/, ... (mevcut servisler)
  scripts/           GPO PowerShell betikleri (yalnızca bunlar çalıştırılır)
scripts/             Kurulum betikleri (agent, sır koruma)
docs/                Bu belgeler + ADR/
```
