# SystemPanel — Ürün Lisanslama (teknik)

Kapalı ağda çalışan kurumlar için **çevrimdışı, imzalı lisans dosyası** modeli. Ürün internete bağlanmaz ve lisans sunucusuna erişmez.

## Nasıl çalışır?

```
Üretici (siz)                                           Müşteri sunucusu
──────────────                                          ─────────────────
splicense keygen  → license-signing.private.pem  (GİZLİ, sizde kalır)
                  → license-public.pem  ──► ürüne gömülür (derleme sırasında)
splicense issue   → kurum.lic  ─────────────────────────► Lisans ekranından yüklenir
                                                         → %ProgramData%\SystemPanel\license.lic
                                                         → gömülü açık anahtarla doğrulanır (ECDSA P-256)
```

- Lisans içeriği: lisans no, kurum, sürüm (Evaluation/Standard/Enterprise), düzenlenme tarihi, bitiş (veya süresiz), destek bitişi, agent sınırı, modüller.
- İçerik değiştirilirse imza bozulur ve lisans reddedilir. Müşteri kendi anahtarıyla lisans üretemez, çünkü açık anahtar derlemeye gömülüdür ve yapılandırmadan değiştirilemez.

## Lisans politikası

Bu ürün kritik altyapıyı yönetir. Lisans sorunu yüzünden müşterinin AD'sine erişememesi veya izlemenin durması kabul edilemez (hem güvenlik hem hukuki risk). Bu nedenle politika bilinçli olarak **yumuşaktır**:

| Durum | Davranış |
|---|---|
| Geçerli | Tam işlev; agent sayısı lisanstaki sınıra kadar |
| Süresi doldu, ek süre içinde (30 gün) | Tam işlev, yöneticilere uyarı |
| Süresi doldu / lisans yok / geçersiz | **Mevcut her şey çalışmaya devam eder.** Yalnızca yeni agent kaydı değerlendirme sınırına (5 etkin agent) iner. Uyarı gösterilir |
| Açık anahtar gömülü değil (geliştirme yapısı) | Kısıtlama yok, uyarı yok |

İleride modül bazlı kısıtlama (`Features`) eklenebilir. Bu durumda da kısıtlama yalnızca **yeni** işlemleri etkilemeli ve okuma/acil müdahale her zaman açık kalmalıdır.

## Üretici işlemleri

```bash
# 1) Bir kez: anahtar çifti (güvenli, çevrimdışı bir makinede)
dotnet run --project tools/SystemPanel.LicenseTool -- keygen --out <güvenli-klasör>

# 2) Açık anahtarı ürüne ekleyin ve ürün yapısını derleyin
cp <güvenli-klasör>/license-public.pem src/SystemPanel/Licensing/license-public.pem

# 3) Müşteri başına lisans
dotnet run --project tools/SystemPanel.LicenseTool -- issue \
  --key <güvenli-klasör>/license-signing.private.pem \
  --customer "<Kurum adı>" --edition Enterprise \
  --expires 2027-12-31 --support 2027-12-31 --max-agents 250 \
  --out <kurum>.lic

# Kontrol
dotnet run --project tools/SystemPanel.LicenseTool -- verify --public license-public.pem --license <kurum>.lic
```

- `.gitignore`: `*.private.pem` ve `*.lic` repoya girmez.
- **Özel anahtar:** Şifreli ortamda, en az iki yedekle (biri çevrimdışı) saklayın. Kaybolursa yeni lisans üretemezsiniz. Sızarsa herkes lisans üretebilir: anahtarı değiştirip yeni ürün sürümü çıkarmanız gerekir.
- Her verilen lisansı (lisans no, kurum, tarihler) ayrı bir kayıtta tutun (fatura/sözleşme ile eşleştirme).

## Müşteri işlemleri

**Lisans** menüsü (`settings.view` görüntüler, `settings.manage` yükler). Yükleme audit'lenir (`license.install`). Alternatif: dosyayı `%ProgramData%\SystemPanel\license.lic` olarak kopyalamak (`Licensing:LicenseFilePath` ile değiştirilebilir).

## Bilinen sınırlar

- Lisans makineye bağlı değildir; aynı dosya başka bir kurulumda da çalışır. Kurum içi kullanımda bu kabul edilebilir: sözleşme ve agent sınırı esas kontroldür. Gerekirse ileride kurulum kimliği (installation id) eklenebilir.
- Sistem saatini geri almak süre kontrolünü atlatabilir. Yumuşak politika nedeniyle bu risk düşüktür; sözleşmeyle güvence altına alınır.
- Bu bir kopya koruması değildir: kaynak koduna sahip biri kontrolü kaldırabilir. Asıl koruma sözleşme (EULA), telif hakkı ve kaynak kodun müşteriye verilmemesidir.
