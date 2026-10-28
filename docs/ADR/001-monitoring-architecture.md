# ADR-001: İzleme Mimarisi — Agent-öncelikli telemetri

| | |
|---|---|
| Durum | Kabul edildi |
| Tarih | 2026-10-27 |
| Kapsam | SystemPanel web, AgentSystemPanel, SystemPanel.Dashboard |

## Bağlam

İlk sürümde aynı sunucu metriklerini toplayan üç ayrı yol vardı:

1. **Agent push:** Windows servisi metrikleri `api/agentmetrics/ingest` adresine gönderiyor. Uç nokta anonim, sunucuyu otomatik kaydediyor ve `LatestServerMetrics`/`ServerMetricHistory`/`ServerDiskHistory`/`AlarmLogs` tablolarına yazıyor. Ana sayfa, sunucu listesi ve alarmlar bu verilerle çalışıyor.
2. **Uzak WMI / ServiceController (pull):** Web sunucusu her istekte DP sunucularına WMI/RPC ile bağlanıp servis durumu okuyor ve servis başlatıp durduruyor (`SccmLogService`, `DpServiceStatusService`).
3. **WinRM/PowerShell (pull):** `ServerMonitoringService`. DI'ye kayıtlı, hiçbir yerden çağrılmıyor. `SkipCACheck` ve sunucu adı enjeksiyonu içeriyor.

Hedef ortam kapalı ve çok lokasyonlu bir ağdır (onlarca birim/lokasyon). Merkezden her sunucuya WMI/RPC açmak:
- Güvenlik duvarında geniş RPC/DCOM aralıkları ve her sunucuda yönetici yetkili tek bir paylaşılan hesap gerektirir. Bu hesap ele geçirilirse tüm sunucular etkilenir.
- İstek thread'lerinde 30+ saniye süren çağrılar oluşturur (ölçeklenmez).
- Sunucu erişilemediğinde "veri yok" ile "sunucu kapalı" arasında ayrım yapılamaz.

## Karar

**Agent-öncelikli (push) telemetri esas alınır.**

```
Windows Server
  └─ SystemPanel Agent (Windows Service, kendi makine kimliği)
        │  HTTPS, agent başına kimlik + gizli anahtar (yalnızca hash'i saklanır)
        ▼
SystemPanel API  /api/v1/agents/{heartbeat,metrics}
        │  doğrulama → MetricsIngestionService
        ▼
SQL Server (LatestServerMetrics, *History, Agents)  ──►  Retention servisi
        │
        ├─► Web UI (Razor, ileride SignalR push)
        └─► WPF Dashboard (WebView2 panelleri)
```

1. Ingest yalnızca **kimliği doğrulanmış agent'lardan** kabul edilir. Her agent'ın bir `AgentId` + gizli anahtarı vardır; anahtar bir kez gösterilir ve DB'de yalnızca SHA-256 hash'i tutulur. Agent yalnızca kendisine kayıtlı hostname adına veri gönderebilir.
2. Eski anonim uç (`api/agentmetrics/ingest`) geçiş süreci için korunur, ama **varsayılan olarak kapalıdır** (`Monitoring:AllowLegacyAnonymousIngest=false`). Eski agent'lar yeni sürüme geçerken geçici olarak açılabilir; her istek uyarı olarak loglanır.
3. Ingest mantığı controller'dan alınıp `MetricsIngestionService` içine taşınır. Eski ve yeni uç aynı servisi kullanır, tablolar ve anlamları **değişmez**.
4. Metrik geçmişi için **retention** (saklama süresi) servisi eklenir. Veri silme işlemi olduğu için **varsayılan olarak kapalıdır** ve açıkça etkinleştirilmelidir.
5. **Uzak WMI** yalnızca şu durumlar için korunur: SCCM DP canlı detay ekranı ve agent kurulamayan cihazlar. Bu bir **fallback** ve keşif aracıdır, izleme kaynağı değildir. Servis kontrolü (start/stop) allowlist, izin, scope ve audit kontrolünden geçer. Uzun vadede agent komut modeline taşınır (aşağıda).
6. **WinRM `ServerMonitoringService` kaldırılacak** (kullanılmıyor, güvensiz). Hiçbir uç ona erişmiyor; ROADMAP temizliğinde silinecek.
7. **Uzaktan eylemler** (servis restart vb.) ileride yalnızca **allowlist'li komut modeliyle** agent üzerinden yapılacak: sunucu bir `AgentCommand` (ör. `RestartService{name}`) kuyruğa koyar, agent heartbeat'te çeker, kendi yerel allowlist'ine göre uygular ve sonucu raporlar. **Rastgele shell/PowerShell çalıştırma hiçbir zaman desteklenmeyecek.**

## Değerlendirilen alternatifler

| Seçenek | Artı | Eksi | Sonuç |
|---|---|---|---|
| Yalnızca merkezi WMI/WinRM pull | Sunucuya kurulum gerektirmez | Geniş ağ açıklığı, paylaşılan güçlü hesap, ölçeklenmez, istek thread'inde bloklar | Reddedildi |
| Prometheus/Grafana, Zabbix gibi harici ürün | Olgun | Kapalı ağda ek altyapı ve işletim yükü; AD/il scope'u ve audit ile entegre değil | Şimdilik reddedildi; ileride export ile entegre edilebilir |
| Mesaj kuyruğu (RabbitMQ vb.) | Yüksek hacim, dayanıklılık | Kapalı ağda ek bileşen; mevcut hacimde gereksiz | Reddedildi (Agent yerel tampon + yeniden deneme yeterli) |
| **Agent push + merkezi API (seçilen)** | Kimliği doğrulanmış uçlar, sunucu başına en az yetki, offline tamponlama, ölçeklenir | Her sunucuya kurulum ve agent sürüm yönetimi gerekir | **Kabul** |

## Sonuçlar

- (+) Ingest kimliği doğrulanmış ve izlenebilir hale gelir. "Sunucu kapalı" ile "agent sessiz" durumları `LastSeenAt` ile ayırt edilebilir.
- (+) Web sunucusunun her sunucuya yönetici erişimine ihtiyacı azalır.
- (−) Her sunucuya agent kurulumu ve anahtar dağıtımı gerekir (bkz. [AGENT.md](../AGENT.md)).
- Takip: alarm motoru (kural, süre, cooldown, ack/resolve) ROADMAP P1; SignalR push ROADMAP P2; agent komut modeli ROADMAP P2.
