# SystemPanel — Veritabanı

SQL Server, EF Core 9 (`ApplicationDbContext`). Identity tabloları + uygulama tabloları.

## Migration'lar

| Migration | İçerik |
|---|---|
| `InitialCreate` | Tüm şema (Identity, yetki/audit, izleme, agent, dashboard ve diğer modül tabloları) |

Kural: yeni migration'lar yalnızca tablo/kolon **ekler**. Veri silen veya değiştiren migration yazılmaz; gerekiyorsa ayrı, gözden geçirilmiş bir veri taşıma script'i hazırlanır.

## ⚠️ Müşteri ortamına uygulamadan önce

1. **Önce yedek alın** (tam yedek + test ortamında geri yükleme denemesi).
2. Mevcut sürümü kontrol edin:
   ```sql
   SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;
   ```
3. Uygulanacak SQL'i script olarak üretip gözden geçirin. Doğrudan `database update` çalıştırmayın:
   ```bash
   DOTNET_ROLL_FORWARD=Major dotnet ef migrations script <son-uygulanan> --idempotent \
     --project src/SystemPanel -o migrate.sql
   ```
4. Script'i DBA ile birlikte, bakım penceresinde uygulayın.

Geliştirme makinesinde: `dotnet ef database update --project src/SystemPanel` (bağlantı dizesi `appsettings.Development.json` veya `ConnectionStrings__DefaultConnection`).

Uygulama açılışta otomatik migration **yapmaz**. Migration uygulanmamışsa `RolePermissions` tohumlanamaz; yetkiler bellekteki varsayılan eşlemeyle çalışır (uyarı loglanır).

## Tablolar (özet)

| Grup | Tablolar |
|---|---|
| Kimlik | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, ... |
| Yetki / audit | `RolePermissions` (RoleName, Permission), `AuditEvents` |
| İzleme | `Agents`, `ServerInfos`, `LatestServerMetrics`, `ServerMetricHistory`, `ServerDiskHistory`, `AlarmLogs` |
| Dashboard | `SystemDashboardLinks` |
| Diğer | Duyurular, AD önbellek tabloları (`AdUsers` vb.), `SccmDpSnapshot`, harita birimleri, loglar |

`SccmDpSnapshot` tablosu dış bir süreçle (SCCM veritabanından) doldurulur; kurulum belgesine eklenecek (ROADMAP).

## Saklama (retention)

`MetricsRetentionService` varsayılan olarak **kapalıdır** (`Monitoring:Retention:Enabled=false`). Açıldığında:

| Ayar | Varsayılan |
|---|---|
| `MetricHistoryDays` | 30 — `ServerMetricHistory`, `ServerDiskHistory` |
| `AlarmDays` | 180 — `AlarmLogs` |
| `RunEveryHours` | 6 |
| `BatchSize` | 5000 (küçük partiler, uzun kilit yok) |

Silme geri alınamaz. Açmadan önce saklama gereksinimlerini (mevzuat, raporlama) netleştirin. `AuditEvents` otomatik silinmez.

## Test veritabanı

Entegrasyon testleri SQLite in-memory + `EnsureCreated` kullanır (migration'lar SQL Server'a özgüdür). Bu nedenle SQL Server'a özgü davranışlar (ör. `HasDefaultValueSql`) testlerde doğrulanmaz.
