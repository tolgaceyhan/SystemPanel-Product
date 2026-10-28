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
- **Lisans:** çevrimdışı imzalı lisans dosyası (ECDSA P-256), lisans ekranı, agent sınırı; lisans sorunu mevcut işlemleri durdurmaz.
- **Marka:** ürün adı ve logolar yapılandırılabilir (`Branding`).
- **Organizasyon yapısı:** birim etiketi yapılandırılabilir (`Organization:UnitLabel`; il, bölge, şube...). Türkiye il haritası opsiyonel modül (`Organization:Map:Enabled`, varsayılan kapalı).
- **Kurulum:**
  - İlk kurulum sihirbazı (`/setup`, tek kullanımlık belirteç)
  - IIS kurulum/güncelleme ve kaldırma betikleri
  - Sürüm paketi üretimi (`build/Build-Release.ps1`): idempotent SQL script, EF migration paketi, isteğe bağlı kod imzalama, SHA-256 özeti
  - CI'da Windows paket doğrulaması
- Duyuru, SMS, dosya yönetimi, log ve istatistik modülleri.
