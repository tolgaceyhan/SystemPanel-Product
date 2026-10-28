# Değişiklik Günlüğü

Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/). Sürümleme: [SemVer](https://semver.org/lang/tr/).

## [1.0.0-preview.1] — Yayınlanmadı

İlk ürün sürümü.

### Özellikler
- **Yetkilendirme:** izin kataloğu, rol-izin matrisi (veritabanında, arayüzden yönetilir), AD grup → rol eşleme, organizasyonel kapsam (RFC 4514 DN, fail-closed), ayrıcalıklı hesap koruması.
- **Audit:** tüm ayrıcalıklı işlemler için denetim kaydı (maskeleme, correlation id) ve Denetim Kayıtları ekranı.
- **Active Directory:** kullanıcı arama/oluşturma/düzenleme, parola sıfırlama, kilit açma, etkinleştirme/devre dışı bırakma, OU taşıma, grup üyeliği; bilgisayar listesi; tekil, audit'li LAPS gösterimi (Windows LAPS ve legacy LAPS).
- **GPO:** listeleme, ayrıntı, bağlama/kaldırma, durum, yedekleme; silme/içe aktarma yapılandırmayla açılır. Yalnızca sabit, parametreli PowerShell betikleri.
- **SCCM:** DP görünümü, allowlist'li servis kontrolü.
- **İzleme:** agent kaydı (tek seferlik anahtar), kimlik doğrulamalı v1 heartbeat/metrics API, rate limit, eşik alarmları, opsiyonel saklama servisi.
- **Agent:** Windows servisi; HTTPS zorunlu, DPAPI ile korunan anahtar, diske kalıcı tampon, geri çekilme + jitter, kurulum/kaldırma betikleri.
- **NOC Dashboard:** WPF + WebView2; yeniden bağlanma, son geçerli yapılandırma önbelleği, ProgramData altında log.
- **Platform:** HTTPS/HSTS, güvenli cookie, DPAPI korumalı yapılandırma değerleri, DataProtection anahtar koruması, güvenlik başlıkları, CSRF, ProblemDetails, health uçları.
- **Otomasyon (runbook) motoru — ADR-002 Faz 1:**
  - İmzalı runbook paketleri (ECDSA P-256) ve güvenilen anahtar deposu (üretici + kurum anahtarları, iptal)
  - Parametre şeması ve kapsam doğrulaması
  - Yüksek riskte zorunlu önizleme (dry-run)
  - Dört göz onayı (talep eden onaylayamaz; onaylayanın kapsamı kontrol edilir), onay süresi
  - Arka plan çalıştırıcısı, çıktı maskeleme, eksiksiz audit
  - Ekranlar: katalog, talep, onay, sonuç, yönetim
  - Üç yerleşik runbook (kullanıcı işten çıkış, pasif bilgisayar raporu, parola süresi raporu)
  - `splicense runbook-*` komutları; sürüm paketinde runbook imzalama
  - İzinler: `automation.view/run/approve/manage`. Admin rolü her zaman tüm izinlere sahiptir.
- **Otomasyon — ADR-002 Faz 2 (agent üzerinde çalıştırma):**
  - Runbook'lar seçilen sunucudaki agent'ta çalışır. Bağlantıyı agent başlatır, ek port açılmaz.
  - Önizleme agent'ta yapılır. Hedef agent kullanıcının biriminde olmalı (talep eden ve onaylayan için).
  - Agent tarafı kontroller: otomasyon varsayılan kapalı, yerel izin listesi, agent'ın kendi güvendiği anahtarla imza doğrulaması, parametrelerin yeniden doğrulanması.
  - Zamanında alınmayan veya sonucu bildirilmeyen işler kapanır ve yeniden çalıştırılmaz.
  - Üç agent runbook'u: servis yeniden başlatma, Windows Temp temizliği, tanılama özeti.
  - Agent kurulum betiğine `-AllowedRunbooks` ve `-TrustedRunbookKeyFiles` parametreleri eklendi.
  - Ortak runbook çalışma zamanı `SystemPanel.Contracts` projesine taşındı.
- **Lisans:** çevrimdışı imzalı lisans dosyası (ECDSA P-256), lisans ekranı, agent sınırı; lisans sorunu mevcut işlemleri durdurmaz.
- **Marka:** ürün adı ve logolar yapılandırılabilir (`Branding`).
- **Organizasyon yapısı:** birim etiketi yapılandırılabilir (`Organization:UnitLabel`; il, bölge, şube...). Türkiye il haritası opsiyonel modül (`Organization:Map:Enabled`, varsayılan kapalı).
- **Kurulum:**
  - İlk kurulum sihirbazı (`/setup`, tek kullanımlık belirteç)
  - IIS kurulum/güncelleme ve kaldırma betikleri
  - Sürüm paketi üretimi (`build/Build-Release.ps1`): idempotent SQL script, EF migration paketi, isteğe bağlı kod imzalama, SHA-256 özeti
  - CI'da Windows paket doğrulaması
- **Kapalı ağ uyumu:** harici (CDN) kaynak yok; CSP tüm kaynakları uygulamanın kendisiyle sınırlar; bunu doğrulayan test var.
- Duyuru, SMS, dosya yönetimi, log ve istatistik modülleri.
