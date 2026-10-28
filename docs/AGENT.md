# SystemPanel Agent

Windows sunucularda çalışan izleme servisi (`SystemPanel.Agent.exe`, servis adı `SystemPanelAgent`). CPU, bellek ve tüm sabit disklerin doluluk bilgisini toplar ve SystemPanel'e HTTPS üzerinden gönderir. Mimari karar: [ADR-001](ADR/001-monitoring-architecture.md).

## Akış

```
Collectors (cpu, memory, disk) ──► MetricBuffer (sınırlı, diske kalıcı) ──► AgentApiClient
                                                                               │ POST /api/v1/agents/metrics
                                                                               │ POST /api/v1/agents/heartbeat
                                                                               ▼
                                                     2xx → tampondan sil │ 401/403 → dur, logla (anahtar hatalı / agent kapalı)
                                                     408/429/5xx/ağ hatası → üstel geri çekilme + jitter (en fazla MaxBackoffSeconds)
                                                     diğer 4xx → kaydı at (sunucu reddetti)
```

- Tampon dolarsa en eski ölçüm atılır (`BufferMaxItems`). Servis yeniden başlarsa tampon diskten okunur. Sunucu kapalıyken veri kaybı en aza iner.
- Sunucu adresi koda gömülü değildir. Kurulumda verilir; HTTPS zorunludur (`AllowInsecureHttp=true` yalnızca geçiş için).

## 1. Sunucuda agent kaydı

1. SystemPanel → **Agent'lar** → **Yeni agent** (`monitoring.manage`).
2. Sunucu adı (hostname, NetBIOS) ve isteğe bağlı il girin.
3. Ekranda **bir kez** gösterilen `AgentId` ve `AgentKey` değerlerini alın. Sunucu yalnızca anahtarın SHA-256 özetini saklar; anahtar kaybolursa **Anahtarı yenile** kullanılır (eski anahtar hemen geçersiz olur).

## 2. Kurulum

Yayın paketi oluşturma (geliştirici makinesi):

```powershell
dotnet publish src/AgentSystemPanel -c Release -o .\publish
```

Hedef sunucuda (yönetici PowerShell):

```powershell
.\Install-SystemPanelAgent.ps1 -ServerUrl https://<systempanel-sunucusu> -AgentId <guid> -SourcePath .\publish
# AgentKey güvenli olarak sorulur (SecureString), DPAPI LocalMachine ile şifrelenip yazılır.
```

Betik şunları yapar:
- Dosyaları `%ProgramFiles%\SystemPanel\Agent` altına kopyalar.
- `%ProgramData%\SystemPanel\Agent\agentsettings.json` dosyasını yazar (`ServerUrl`, `AgentId`, `AgentKey=dpapi:...`). ACL: SYSTEM, Administrators ve servis hesabı.
- Servisi `NT SERVICE\SystemPanelAgent` sanal hesabıyla kurar (yönetici yetkisi yok), hesabı `Performance Monitor Users` grubuna ekler.
- Hata durumunda otomatik yeniden başlatmayı ayarlar (`sc failure`).

Kaldırma: `.\Uninstall-SystemPanelAgent.ps1` (ayar ve tampon korunur; `-RemoveData` ile silinir). SystemPanel'deki kaydı ayrıca **Devre dışı bırak** ile kapatın.

## 3. Yapılandırma

Öncelik sırası: `appsettings.json` (exe yanında, sır içermez) → `%ProgramData%\SystemPanel\Agent\agentsettings.json` → ortam değişkenleri (`SYSTEMPANEL_AGENT_Agent__ServerUrl` vb.).

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `Agent:ServerUrl` | — | SystemPanel kök adresi (https) |
| `Agent:AgentId` | — | Kayıtta verilen GUID |
| `Agent:AgentKey` | — | `dpapi:...` (önerilir) veya düz değer |
| `Agent:IntervalSeconds` | 30 | Ölçüm aralığı |
| `Agent:HeartbeatIntervalSeconds` | 60 | Heartbeat aralığı |
| `Agent:TimeoutSeconds` | 15 | HTTP zaman aşımı |
| `Agent:EnabledCollectors` | cpu, memory, disk | Etkin toplayıcılar |
| `Agent:BufferMaxItems` | 1000 | Tampon üst sınırı |
| `Agent:BufferDirectory` | `%ProgramData%\SystemPanel\Agent\buffer` | Tampon klasörü |
| `Agent:MaxBackoffSeconds` | 300 | En uzun bekleme |
| `Agent:AllowInsecureHttp` | false | `http://` sunucu adresine izin |

Geçersiz yapılandırmada servis açılmaz ve nedeni Windows Event Log'a (`SystemPanelAgent`) yazılır.

## 4. Sunucu tarafı

| Uç | Kimlik | Yanıt |
|---|---|---|
| `POST /api/v1/agents/heartbeat` | `X-Agent-Id` + `X-Agent-Key` | 200 + `RecommendedIntervalSeconds` |
| `POST /api/v1/agents/metrics` | aynı | 202. Doğrulama hatası 400 (yüzde 0–100, en fazla 64 disk). 5 dk'dan ileri veya 1 günden eski zaman damgası sunucu saatiyle değiştirilir |
| `POST /api/v1/agents/commands/next` | aynı | 200 + iş veya 204 (iş yok) |
| `POST /api/v1/agents/commands/{id}/result` | aynı | 204. İş bu agent'ın değilse 404, beklenmeyen aşamadaysa 409 |
| `POST /api/agentmetrics/ingest` (eski, anonim) | yok | **410 Gone**. `Monitoring:AllowLegacyAnonymousIngest=true` ile geçici olarak açılabilir |

- Agent yalnızca kayıtlı olduğu hostname adına bildirim yapabilir (aksi 403).
- Devre dışı bırakılan agent 401 alır.
- Başarısız kimlik doğrulama denemeleri kısıtlanarak audit'lenir.
- İl bilgisi agent'tan değil, kayıttan (`Agents.Province`) gelir.

## 5. Otomasyon (runbook) işleri

Agent, SystemPanel'den onaylanmış runbook işlerini çekip çalıştırabilir. Bu özellik **varsayılan olarak kapalıdır** ve yalnızca yerel izin listesindeki, güvenilen anahtarla imzalı runbook'ları çalıştırır.

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `Agent:Automation:Enabled` | false | Runbook işlerini al |
| `Agent:Automation:AllowedRunbooks` | [] | Bu sunucuda çalışabilecek runbook kimlikleri |
| `Agent:Automation:TrustedKeyFiles` | [] | Ek güvenilen imzalama açık anahtarları (PEM). Üretici anahtarı gömülüdür |
| `Agent:Automation:PollSeconds` | 15 | İş kontrol aralığı |
| `Agent:Automation:PowerShellPath` | powershell.exe | |
| `Agent:Automation:MaxOutputBytes` | 262144 | Sunucuya gönderilen çıktı sınırı |

Ayrıntılar ve yetki önerileri: [AUTOMATION.md](AUTOMATION.md#agent-üzerinde-çalışan-runbooklar-faz-2).

## 6. Eski agent'tan geçiş

Kimlik doğrulamasız eski agent protokolünü (`api/agentmetrics/ingest`) kullanan kurulumlar için:

1. Tüm sunucular için agent kaydı oluşturun.
2. Gerekirse geçiş süresince `AllowLegacyAnonymousIngest=true` yapın.
3. Yeni agent'ı dağıtın (GPO/SCCM paketi ile `Install-SystemPanelAgent.ps1`).
4. **Agent'lar** ekranında tüm sunucuların "son görülme" bilgisini doğrulayın.
5. `AllowLegacyAnonymousIngest=false` yapın.
