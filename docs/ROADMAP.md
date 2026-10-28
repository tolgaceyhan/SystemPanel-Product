# SystemPanel — Yol Haritası

Önem: **P1** yüksek · **P2** orta · **P3** düşük.

## Mevcut sürüm (1.0 önizleme)

- İzin tabanlı yetki, organizasyonel kapsam (fail-closed), audit
- AD kullanıcı/bilgisayar işlemleri, audit'li LAPS gösterimi, GPO (sabit betikler), SCCM DP görünümü
- Agent tabanlı izleme (kimlik doğrulamalı v1 API), NOC Dashboard
- HTTPS, DPAPI korumalı yapılandırma, güvenlik başlıkları
- Çevrimdışı imzalı ürün lisansı
- Birim, entegrasyon, agent ve dashboard testleri

## 1.0 sürümü öncesi (P1)

1. **Kurulum paketi:** sihirbaz (MSI), veritabanı oluşturma, ilk yönetici, sertifika kontrolü.
2. **Kod imzalama:** web, agent, dashboard ve kurulum paketleri.
3. ✅ **Genelleştirme:** yapılandırılabilir birim etiketi; Türkiye haritası opsiyonel modül.
4. **CDN bağımlılıklarının yerelleştirilmesi** (Chart.js, toastr, Bootstrap Icons) ve tam CSP.
5. Harita/istatistik ekranlarında stub veri yerine gerçek veri (MapUnitService, DP listesi). `SccmDpSnapshot` kaynağının ürüne dahil edilmesi.
6. İzleme: çevrimdışı uyarısı, alarm onaylama ve susturma, sunucu bazında eşikler.
7. Audit kayıtlarının SIEM'e aktarımı (syslog/CEF) ve saklama süresi.
8. Ölü servislerin kaldırılması (`ServerMonitoringService` vb.).
9. Bağımsız sızma testi ve bulguların kapatılması.
10. Demo/laboratuvar ortamı (örnek AD ve örnek veri).

## 1.x (P2)

1. **Otomasyon motoru (runbook):** imzalı katalog, parametre şeması, önizleme (dry-run), dört göz onayı, zamanlama, olay tetikleyici, audit. Keyfi komut çalıştırma yok.
2. Kullanıcı yaşam döngüsü otomasyonu (işe giriş/çıkış), self-servis parola sıfırlama.
3. GPO: şablondan oluşturma, ayar raporu, değişiklik onayı.
4. SCCM: koleksiyon/dağıtım durumu (salt okunur), istemci sağlık raporu.
5. Toplu işlemler (CSV): önizleme + onay + audit.
6. Şifreli Windows LAPS desteği.
7. Dashboard yapılandırma ucunda kimlik doğrulama.
8. CSV formula injection koruması; tarayıcıda `Math.random` ile parola üretiminin kaldırılması.
9. Modül bazlı lisanslama (yalnızca yeni işlemleri sınırlar).

## 2.x (P3)

1. Çoklu domain/orman desteği.
2. Agent: otomatik güncelleme (imzalı paket), ek toplayıcılar (servis durumu, olay günlüğü özeti, sertifika süreleri).
3. Uyumluluk raporları (CIS benchmark vb.).
4. OpenAPI sözleşmesi; tüm `/api` uçlarının v1 altına alınması.
5. Controller'lardaki doğrudan `DbContext` kullanımının servis katmanına taşınması.
6. Varsayılan route'un gözden geçirilmesi (`/Audit` gibi kısa adresler 404; menü `/X/Index` kullanıyor).
7. Çoklu dil (TR/EN).
