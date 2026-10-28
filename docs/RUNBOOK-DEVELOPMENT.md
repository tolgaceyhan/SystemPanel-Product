# Runbook Geliştirme Kılavuzu (Kurum Runbook'ları)

Bu kılavuz, kurumların SystemPanel'e **kendi** otomasyonlarını eklemesini anlatır. Tasarım gerekçesi:
[ADR-002](ADR/002-automation-runbooks.md). Panel tarafındaki kullanım: [AUTOMATION.md](AUTOMATION.md).

> Özet: Runbook = `manifest.json` + `run.ps1`. `sprunbook` ile denetlenir ve kurumun anahtarıyla imzalanır. Panel
> yalnızca güvendiği bir anahtarla imzalanmış paketi kabul eder. Kullanıcı betik yazmaz; yalnızca katalogdan bir runbook
> seçer, şemaya uyan parametreleri girer ve gerekiyorsa onaya gönderir.

## 1. Rol ayrımı

| Rol | Ne yapar | Nerede |
|---|---|---|
| Runbook geliştiricisi | Betiği yazar, test ortamında dener, kaynak kontrolüne koyar | Geliştirme makinesi |
| İmzalayan (sürüm sorumlusu) | Gözden geçirilmiş sürümü `sprunbook sign` ile imzalar | **Panelden ayrı**, erişimi kısıtlı bir makine |
| Otomasyon yöneticisi (`automation.manage`) | Açık anahtarı kaydeder, paketi içe aktarır, gerekirse anahtarı iptal eder | SystemPanel |
| Talep eden / onaylayan | Runbook'u çalıştırır / onaylar (dört göz) | SystemPanel |

Aynı kişinin hem imzalayıp hem onaylaması mümkün olsa da önerilmez. İmza anahtarı panel sunucusunda **bulunmamalıdır**:
paneli ele geçiren biri o zaman yeni runbook imzalayamaz.

## 2. Anahtar

```powershell
sprunbook keygen --out <güvenli-klasör>
#  runbook-org.private.pem  -> GİZLİ (şifreli disk/HSM/çevrimdışı makine; yedekli)
#  runbook-org-public.pem   -> panele ve agent'lara dağıtılır
sprunbook fingerprint --public <güvenli-klasör>\runbook-org-public.pem
```

1. **Otomasyon → Katalog ve anahtarlar → Kurum imzalama anahtarı ekle**: `runbook-org-public.pem` içeriğini yapıştırın.
   Parmak izini `sprunbook fingerprint` çıktısıyla karşılaştırın. Panel özel anahtarı **reddeder**. Yanlışlıkla
   yapıştırılırsa anahtar ifşa olmuş sayılır; yenisini üretin.
2. Agent'ta çalışacak runbook'lar için açık anahtarı agent'lara da dağıtın (`-TrustedRunbookKeyFiles`, bkz. AUTOMATION.md).

**Rotasyon:** Yeni anahtar üretin, panele ekleyin, yeni sürümleri yeni anahtarla imzalayın, eski anahtarı iptal edin.
Kurum içinde anahtar değişebilir; bir runbook kimliği kurumda kalır (bkz. §6).

**Sızıntı:** Paneldeki anahtar kaydını hemen **İptal et**. Sonuçları:
- O anahtarla imzalı paketler içe aktarılamaz, talep edilemez, onaylanamaz ve çalıştırılamaz.
- Henüz başlamamış talepler (onay bekleyen, onaylı/kuyrukta, agent'ın almadığı önizleme) **iptal edilir**.
- Bu sürümlere bağlı zamanlamalar **kapatılır**.
- Hepsi audit'e yazılır (`automation.key.revoke`, `automation.run.cancelled`, `automation.schedule.disable`).
- Çalışmakta olan veya agent'a teslim edilmiş işler durdurulamaz; sonuçları kaydedilir.
- Agent'lardaki anahtar dosyasını da kaldırın. Agent kendi güven listesini kullanır.

## 3. İskelet

```powershell
sprunbook new --id org.kurum.iis.recycle --executor agent --risk medium --out runbooks --title "IIS havuzunu geri dönüştür"
```

- **Kimlik:** `org.<kurum>.<alan>.<ad>` biçiminde, küçük harf, nokta ile ayrılır. Örnek: `org.kurum.ad.user-unlock`.
  `org.` ön eki kurumlara ayrılmıştır. Üretici kataloğu bu öneki kullanmaz.
- **Klasör adı** kimlikle aynı olmalıdır.
- Hazır örnekler: [`runbooks/examples/`](../runbooks/examples/README.md). Sürüm paketinde `runbook-examples\` altında bulunur.

## 4. Manifest

```jsonc
{
  "id": "org.kurum.ad.user-unlock",
  "version": "1.0.0",                  // her değişiklikte artırın; aynı sürüm farklı içerikle yüklenemez
  "title": "Kullanıcı hesap kilidini aç",
  "description": "Ne yapar, neyi DEĞİŞTİRİR, önizleme ne gösterir. Onaylayan kişi bunu okur.",
  "executor": "server",                // server: web sunucusunda (AD) | agent: hedef sunucuda
  "targetType": "ad-user",             // none | ad-user | ad-computer | ad-ou | server (yalnızca agent)
  "risk": "medium",                    // low: onaysız | medium: 1 onay | high: 1 onay + zorunlu önizleme
  "supportsDryRun": true,
  "idempotent": true,
  "timeoutSeconds": 60,                // 10-3600
  "parameters": [
    { "name": "UserDn", "type": "ad-dn", "label": "Kullanıcı (DN)", "required": true, "inScope": true }
  ]
}
```

### Risk seçimi

| Risk | Ne zaman | Davranış |
|---|---|---|
| `low` | Yalnızca okuyan raporlar | Onaysız çalışır. Zamanlama ve alarmla otomatik çalışabilir |
| `medium` | Geri alınabilir, sınırlı değişiklik (kilit açma, servis/havuz yeniden başlatma) | 1 onay. Zamanlaması dört göz onayıyla etkinleşir. Alarm yalnızca onay talebi açar |
| `high` | Geri alınması zor veya geniş etkili (hesap kapatma, grup temizliği, taşıma) | 1 onay + önizleme zorunlu. Zamanlanmışsa **her çalıştırmada** onay ister |

Emin değilseniz bir üst düzeyi seçin. Risk düzeyi onay politikasını belirler; düşürmek dört gözü atlatmak demektir.

### Parametre türleri

Türler kapalı bir listedir. Değerler betiğe `-File` + ayrı argüman olarak geçer, asla komut metnine birleştirilmez.
`"`, kontrol karakteri içeren veya `-` ile başlayan değerler her türde reddedilir.

| Tür | Alanlar | Betikte |
|---|---|---|
| `string` | `maxLength` (vars. 256), `pattern` (değerin **tamamı** uymalı) | `[string]$Ad` (+ aynı `ValidatePattern`) |
| `int` | `min`, `max` | `[int]$Ad` (+ `ValidateRange`) |
| `bool` | `default` | **`[switch]$Ad`**: true ise yalnızca `-Ad` geçer |
| `enum` | `values` (en fazla 100) | `[ValidateSet(...)][string]$Ad` |
| `ad-dn` | `inScope` (vars. true) | `[string]$Ad`. DN olarak ayrıştırılır, talep edenin ve onaylayanın kapsamında olmalıdır. **Agent runbook'larında kullanılamaz** |
| `hostname` | | `[string]$Ad`. DNS ad biçimi |

İpuçları:
- `string` parametrelerine **her zaman** `pattern` verin. `sprunbook lint` eksikse uyarır.
- Manifestteki doğrulamayı betikte de tekrarlayın (`ValidatePattern`, `ValidateRange`, `ValidateSet`). İki katmanlı savunma sağlar.
- `DryRun` adı ayrılmıştır, parametre olarak tanımlanamaz.

## 5. Betik kuralları

```powershell
<# kısa açıklama #>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UserDn,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# 1) Oku ve planı hazırla (DEĞİŞİKLİK YOK)
# 2) if ($DryRun) { plan JSON; exit 0 }
# 3) Uygula; sonucu JSON olarak yaz
```

**Zorunlu** (`sprunbook lint` hata verir, imzalamaz):
- `param(...)` bloğu manifestteki her parametreyi içerir. `bool` parametreler `[switch]` olur. Bloktan sonra bir boş satır gelir.
- `supportsDryRun` ile `[switch]$DryRun` tutarlıdır.
- Yasak kalıplar:
  - `Invoke-Expression`/`iex`, `[ScriptBlock]::Create`, `-EncodedCommand`: dinamik kod
  - `Start-Process`, `cmd /c`, `powershell -…`, `pwsh -…`: başka süreç başlatma
  - `Invoke-WebRequest`, `Invoke-RestMethod`, `DownloadString`, `DownloadFile`, `Start-BitsTransfer`: ağdan içerik indirme
  - `Add-Type`, `Set-ExecutionPolicy`
- Betikte sabit parola, anahtar veya belirteç bulunmaz.
- Betik en fazla 256 KB olur.

**Beklenen** (lint uyarı verir):
- `$ErrorActionPreference = 'Stop'`: hata, sıfırdan farklı bir çıkış koduna dönüşür ve çalıştırma `Failed` olur.
- Açıklama doludur, `TODO` kalmamıştır, orta riskte de önizleme vardır.

**Tasarım ilkeleri:**
- **Önizleme gerçekten salt okunur olmalıdır.** `-DryRun` modunda tek bir değişiklik bile yapılmaz. Onaylayan kişi
  önizleme çıktısına bakarak karar verir.
- **Çıktı:** stdout'a tek bir JSON nesnesi yazın (`ConvertTo-Json -Depth 4`).
  - Çıktı boyutu sınırlıdır ve `password`, `secret`, `token` gibi alanlar maskelenir. Yine de parola veya LAPS değerini
    **asla** yazmayın.
  - Kişisel veriyi gerekenle sınırlayın.
- **Kapsamı betikte genişletmeyin.** `-SearchBase` altındaki nesnelerle çalışın; başka OU'lara "atlamayın". Kapsam
  kontrolü parametre değeri üzerinden yapılır.
- **Ayrıcalıklı hesapları koruyun.** `adminCount=1` hesaplarda işlem yapmayın (bkz. `org.example.ad-user-unlock`).
- **İdempotent olun.** Aynı parametrelerle ikinci çalıştırma zarar vermemelidir. Örneğin zaten açık hesabın kilidini açmaya çalışmayın.
- **Altyapı değeri yazmayın.** Sunucu, domain, OU veya grup adı betikte sabit olmaz; parametre olarak alınır.
- **Gizli bilgi gerektiren işlemler:** kimlik bilgisini betiğe veya parametreye koymayın. Gerekiyorsa işlem, çalıştıran
  hesabın (web uygulama havuzu kimliği veya agent servisi) yetkisiyle yapılır. Bu hesaplara yalnızca gereken yetkiyi verin
  (bkz. AUTOMATION.md, Agent hesabının yetkileri).

### Çalışma ortamı

| | `server` | `agent` |
|---|---|---|
| Nerede | SystemPanel web sunucusu | Hedef sunucudaki SystemPanel Agent servisi |
| Kimlik | Uygulama havuzu hesabı (AD yetkileri delege edilmiş) | Agent servis hesabı (varsayılan LocalSystem) |
| Modüller | `ActiveDirectory` (RSAT) vb. sunucuda kurulu olmalı | Hedefte kurulu olmalı (ör. `WebAdministration`) |
| Ek kontrol | Kapsam (`ad-dn`) | Agent'ın **yerel izin listesi** + kendi anahtar listesi; `ad-dn` yok |
| Çağrı | `powershell.exe -NoProfile -NonInteractive -File run.ps1 -Param değer …` (`Automation:PowerShellPath`) | aynı (`Automation:PowerShellPath`, agentsettings.json) |

## 6. Denetle, imzala, yükle

```powershell
sprunbook lint     --source runbooks                      # tüm klasörler; hata varsa çıkış kodu 2
sprunbook sign-all --key <güvenli-klasör>\runbook-org.private.pem --source runbooks --out paketler
sprunbook verify   --public runbook-org-public.pem --package paketler\org.kurum.ad.user-unlock.sprb
```

- `sign`/`sign-all` önce denetler. Hata varsa **hiç paket yazılmaz**.
- **Otomasyon → Katalog ve anahtarlar → Runbook paketi içe aktar.** Panel imzayı, betik özetini ve manifesti yeniden doğrular.
- **Sahiplik kuralları:**
  - `org.` ön ekli kimlikler yalnızca kurum anahtarlarıyla yüklenir.
  - Bir kimlik ilk kez hangi kaynakla (üretici veya kurum) yüklendiyse sonraki sürümleri de aynı kaynaktan gelmelidir.
  - Bu sayede kurum anahtarı yerleşik bir runbook'un yerine geçemez, ürün güncellemesi de kurumun runbook'unu ezemez.
- Agent runbook'u ise kimliği agent'ın `AllowedRunbooks` listesine ekleyin.
- Bir sürümü geri çekmek için katalogdan **Devre dışı bırak** deyin. Paketler silinmez; geçmiş çalıştırmalar hangi sürümün çalıştığını gösterir.

## 7. Test

1. **Yerel deneme** (yalnızca test ortamında): `powershell.exe -File run.ps1 -UserDn "<test DN>" -DryRun`. Ardından `-DryRun` olmadan çalıştırın.
2. **Test panelinde:** paketi yükleyin; talep → önizleme → onay → sonuç akışını izleyin; audit kayıtlarını kontrol edin.
3. **Olumsuz durumlar:** kapsam dışı DN, geçersiz `pattern` değeri, olmayan nesne, kilitli olmayan hesap gibi durumlarda
   betik anlamlı bir hata ya da "yapılacak işlem yok" sonucu döndürmeli.
4. **CI önerisi:** kaynak deposunda `sprunbook lint --source runbooks` ve PSScriptAnalyzer çalıştırın.

## 8. Kontrol listesi (imzalamadan önce)

- [ ] Kimlik `org.` ile başlıyor; klasör adı aynı; sürüm artırıldı
- [ ] Risk düzeyi değişikliğin etkisine uygun; açıklama onaylayanın anlayacağı netlikte
- [ ] Önizleme salt okunur ve planı eksiksiz gösteriyor
- [ ] Tüm `string` parametrelerinde `pattern` var; doğrulama betikte tekrar ediliyor
- [ ] Sabit sunucu/domain/OU/grup adı, parola, anahtar yok
- [ ] Çıktıda parola/LAPS/kişisel veri fazlası yok
- [ ] `sprunbook lint` hatasız; test ortamında denendi; kodu ikinci bir kişi gözden geçirdi
