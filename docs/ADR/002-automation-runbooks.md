# ADR-002: Otomasyon Motoru — İmzalı ve Onaylı Runbook'lar

| | |
|---|---|
| Durum | Kabul edildi. Faz 1 (MVP) ve Faz 2 (agent) uygulandı; kılavuz: [AUTOMATION.md](../AUTOMATION.md) |
| Tarih | 2026-10-28 |
| Kapsam | SystemPanel web, Agent, Contracts |

## Bağlam

Ürünü "yönetim paneli"nden "sistem otomasyon platformu"na taşımak istiyoruz. Örnek kullanımlar:
- Kullanıcı yaşam döngüsü (işe giriş/çıkış)
- Pasif hesap temizliği
- Disk temizliği
- Servis yeniden başlatma
- Sertifika süresi takibi

Bu tür bir motorun en büyük riski bir **uzaktan komut çalıştırma aracına dönüşmesi**dir: ele geçirilen tek bir panel hesabı tüm sunuculara ve AD'ye erişim sağlar. Ürünün temel kuralı korunur: **keyfi komut yok** (CLAUDE.md kural 6, ADR-001 §7).

## Karar

Otomasyon, **imzalı runbook paketleri** ile yapılır. Runbook, sabit bir betik ile o betiğin kabul ettiği parametrelerin şemasından oluşan, sürümlü bir pakettir. Kullanıcı betik yazmaz, yalnızca katalogdan bir runbook seçer ve şemaya uyan parametreleri girer.

```
                ┌────────────── Katalog ──────────────┐
                │ Runbook paketi (imzalı, sürümlü)    │
                │  manifest.json + run.ps1            │
                └───────────────┬─────────────────────┘
 Kullanıcı ── Talep ──► Doğrulama ──► Önizleme (dry-run) ──► Onay (4 göz) ──► Kuyruk
            (parametreler)  (şema, izin,      (değişecek nesneler)   (risk düzeyine göre)   │
                             kapsam, imza)                                                  ▼
                                                       ┌─────────── Yürütücü ───────────┐
                                                       │ AD/GPO: web sunucusu            │
                                                       │   (PowerShellScriptRunner)      │
                                                       │ Sunucu yerel: Agent (komut       │
                                                       │   kuyruğu, yerel allowlist)      │
                                                       └───────────────┬─────────────────┘
                                                                       ▼
                                                     Sonuç + adım logları (maskelenmiş) + Audit
```

### 1. Runbook paketi

```jsonc
{
  "id": "ad.user.offboard",
  "version": "1.2.0",
  "title": "Kullanıcı işten çıkış",
  "description": "Hesabı devre dışı bırakır, gruplardan çıkarır, arşiv OU'suna taşır.",
  "executor": "server",              // server | agent
  "targetType": "ad-user",           // ad-user | ad-computer | server | none
  "risk": "high",                    // low | medium | high
  "requiredPermission": "automation.run",
  "supportsDryRun": true,
  "timeoutSeconds": 300,
  "parameters": [
    { "name": "UserDn",    "type": "ad-dn", "required": true, "inScope": true },
    { "name": "ArchiveOu", "type": "ad-dn", "required": true, "inScope": true },
    { "name": "Reason",    "type": "string", "maxLength": 200, "pattern": "^[\\p{L}\\p{N} .,-]+$" }
  ],
  "script": "run.ps1",
  "scriptSha256": "<hash>"
}
```

- **Parametre türleri kapalı bir listedir:** `string` (pattern + uzunluk), `int` (aralık), `bool`, `enum`, `ad-dn`, `hostname`, `datetime`. Serbest metin betiğe yalnızca parametre olarak geçer, asla komut metnine birleştirilmez (`-File` + `ArgumentList`).
- `ad-dn` + `inScope: true` → değer `OrganizationalScope` ile kapsam kontrolünden geçer (ADR-001 ile aynı kural).
- Betik, `-DryRun` anahtarıyla çağrıldığında **hiçbir değişiklik yapmadan** ne yapacağını JSON olarak döndürmek zorundadır. Yüksek riskli runbook'larda önizleme zorunludur.

### 2. İmza ve güven

| Kaynak | İmza | Kim yükler |
|---|---|---|
| Ürünle gelen yerleşik katalog | Üretici anahtarı (lisans anahtarından **ayrı** bir anahtar) | Ürün güncellemesiyle |
| Kurumun kendi runbook'ları | Kurumun runbook imzalama anahtarı. Açık anahtarı `automation.manage` sahibi kaydeder (audit'lenir) | `automation.manage` |

- İmzasız veya hash'i tutmayan paket **hiçbir koşulda çalışmaz**. Çalıştırma anında yeniden doğrulanır (TOCTOU).
- Paket içeriği veritabanında değişmez (immutable) tutulur. Yeni sürüm yeni kayıttır; geçmiş çalıştırmalar hangi sürümün çalıştığını gösterir.
- Agent tarafında: agent yalnızca **kendi yerel allowlist'indeki** runbook kimliklerini kabul eder (`agentsettings.json`). Paket imzasını kendisi doğrular. Sunucu ele geçirilse bile agent'a allowlist dışında bir iş yaptırılamaz.

### 3. Onay politikası (dört göz)

| Risk | Varsayılan politika |
|---|---|
| low | Onaysız çalışır (ör. rapor üretme) |
| medium | 1 onay |
| high | 1 onay + talep eden ≠ onaylayan + önizleme zorunlu |

- Politika runbook bazında yükseltilebilir, düşürülemez.
- Onaylayan kişi `automation.approve` iznine sahip olmalı ve **hedef kendi kapsamında** olmalıdır.
- Onaylanmayan talepler süre sonunda (varsayılan 24 saat) `Expired` olur.

### 4. Yaşam döngüsü

```
Draft ─► PendingApproval ─► Approved ─► Queued ─► Running ─► Succeeded
   │            │                                    ├─► Failed
   │            ├─► Rejected                          └─► TimedOut
   └────────────┴─► Cancelled / Expired
```

- Her geçiş `AuditEvents` tablosuna yazılır: `automation.run.requested`, `.approved`, `.rejected`, `.started`, `.succeeded`, `.failed`.
- Çalıştırma çıktısı adım adım saklanır. Parola, anahtar ve LAPS alanları `AuditRedactor` kurallarıyla maskelenir.
- Zaman aşımı, iptal ve yeniden deneme (yalnızca `idempotent: true` olan runbook'larda) desteklenir.

### 5. Tetikleyiciler (sonraki faz)

- **Zamanlama** (cron): yalnızca `low` risk ya da önceden onaylanmış parametre kümesi.
- **Olay** (alarm → runbook, ör. disk %90 → temizlik): yalnızca `low` risk ve agent yürütücülü. Diğerleri otomatik çalışmaz, yalnızca onay talebi oluşturur.

### 6. Veri modeli (taslak)

| Tablo | Açıklama |
|---|---|
| `RunbookSigningKeys` | Kayıtlı açık anahtarlar (üretici/kurum), etkin/iptal |
| `Runbooks` | Kimlik, başlık, etkin |
| `RunbookVersions` | Sürüm, manifest JSON, betik içeriği, hash, imza, imzalayan anahtar |
| `RunbookRuns` | Talep: runbook sürümü, parametreler (maskelenmiş), hedef, talep eden, durum, zamanlar, correlation id |
| `RunbookApprovals` | Onay/ret, kim, ne zaman, gerekçe |
| `RunbookRunLogs` | Adım çıktıları (maskelenmiş, boyut sınırlı) |
| `AgentCommands` | Agent yürütücülü işler: agent, run, durum. Agent heartbeat'te çeker (ADR-001 §7) |

### 7. İzinler

`automation.view`, `automation.run`, `automation.approve`, `automation.manage` (katalog ve anahtar yönetimi). Varsayılan olarak yalnızca Admin'de bulunurlar.

## Değerlendirilen alternatifler

| Seçenek | Sonuç |
|---|---|
| Serbest PowerShell konsolu / betik editörü | **Reddedildi.** Uzaktan komut çalıştırma aracıdır; ele geçirilen hesap = tüm ortam |
| Ansible AWX / Rundeck entegrasyonu | Şimdilik reddedildi: kapalı ağda ek altyapı, AD kapsam modeli ve audit ile entegre değil. İleride "harici yürütücü" olarak eklenebilir |
| İmzasız ama "yalnızca Admin yükler" katalog | Reddedildi: Admin hesabının ele geçirilmesi yeterli olurdu. İmza, anahtarın panelden ayrı tutulmasını sağlar |
| WinRM ile merkezden sunuculara çalıştırma | Reddedildi (ADR-001): geniş ağ açıklığı, paylaşılan güçlü hesap |

## Tehditler ve karşı önlemler

| Tehdit | Önlem |
|---|---|
| Panel hesabı ele geçirildi | İmzasız betik çalışmaz. Yüksek riskte 4 göz. Kapsam kontrolü. Her şey audit'lenir |
| Parametre enjeksiyonu | Kapalı tür listesi, pattern doğrulaması, `-File` + `ArgumentList`; komut birleştirme yok |
| Web sunucusu ele geçirildi | Agent kendi allowlist'i ve imza doğrulaması olmadan iş kabul etmez |
| İmza anahtarı sızdı | Anahtar iptali (`RunbookSigningKeys`), iptal edilmiş anahtarla imzalı paketler çalışmaz |
| Çıktıda sır sızıntısı | Maskeleme, boyut sınırı, LAPS/parola alanları hiç kaydedilmez |
| Onay mekanizmasının atlatılması | Durum geçişleri yalnızca servis katmanında; talep eden kendini onaylayamaz (test zorunlu) |

## Uygulama planı

| Faz | İçerik | Kabul ölçütü |
|---|---|---|
| **1 — MVP** | Paket biçimi + imza doğrulama, katalog ekranı, talep → önizleme → onay → çalıştırma (yalnızca `server` yürütücü). Üç yerleşik runbook: kullanıcı devre dışı bırakma + gruplardan çıkarma, pasif bilgisayar raporu, parola sona erme raporu | Tüm durum geçişleri ve 4 göz kuralı entegrasyon testli; imzasız paket reddi testli |
| **2 — Agent** | `AgentCommands` kuyruğu, agent tarafında imza + allowlist doğrulaması. Runbook'lar: servis yeniden başlatma, disk temizliği | Allowlist dışı iş reddi testli |
| **3 — Zamanlama/olay** | Cron tetikleyici, alarm → runbook (yalnızca low risk) | Yüksek riskli runbook'ların otomatik çalışmadığı testli |
| **4 — Kurum runbook'ları** | Kurum imzalama anahtarı kaydı, imzalama aracı (`splicense` benzeri `sprunbook sign`) | Anahtar iptali testli |

## Sonuçlar

- (+) Ürünün ayırt edici özelliği olur: güvenli, kayıt altına alınmış, onaylı otomasyon.
- (+) Mevcut kapsam, izin ve audit altyapısı doğrudan kullanılır.
- (−) Serbest betik esnekliği yoktur. Bu bilinçli bir tercihtir; yeni otomasyon için imzalı paket gerekir.
- (−) İmza anahtarı yönetimi kuruma ek operasyon yükü getirir (aracı ve belgesi sağlanacak).
