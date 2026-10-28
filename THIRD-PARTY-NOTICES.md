# Üçüncü Taraf Bileşenler

SystemPanel aşağıdaki açık kaynak bileşenleri kullanır. Tümü ticari (kapalı kaynak) dağıtıma izin veren lisanslara sahiptir. Ürün internetten (CDN) hiçbir kaynak yüklemez; tüm istemci kütüphaneleri `wwwroot` altındadır. Lisans metinleri paketlerin içinde ve `wwwroot/lib/*/LICENSE*` dosyalarında yer alır; dağıtım paketine bu dosya ile birlikte eklenmelidir.

> Yeni paket eklerken lisansını kontrol edin. **AGPL / GPL / SSPL / "Commons Clause" / ticari kullanımı kısıtlayan lisanslar kabul edilmez.** `itext7` (AGPL) bu nedenle PDFsharp (MIT) ile değiştirildi.

## Sunucu (NuGet)

| Bileşen | Lisans |
|---|---|
| ASP.NET Core, EF Core, Microsoft.Extensions.*, System.* (Microsoft) | MIT |
| Microsoft.Data.SqlClient | MIT |
| Microsoft.PowerShell.SDK | MIT |
| Microsoft.Management.Infrastructure (MMI) | MIT |
| Microsoft.CodeAnalysis.* (Roslyn; sürüm sabitleme) | MIT |
| PDFsharp 6 | MIT |
| CsvHelper | MS-PL veya Apache-2.0 (ikili lisans) |
| Serilog, Serilog.AspNetCore, Serilog.Sinks.* | Apache-2.0 |
| Swashbuckle.AspNetCore | MIT |
| X.PagedList | MIT |

## Agent / Dashboard

| Bileşen | Lisans |
|---|---|
| Microsoft.Extensions.Hosting.WindowsServices, System.Diagnostics.PerformanceCounter, System.Security.Cryptography.ProtectedData | MIT |
| Microsoft.Web.WebView2 (SDK) | Microsoft WebView2 SDK lisansı (BSD tarzı; yeniden dağıtıma izin verir). WebView2 Runtime ayrıca Microsoft koşullarıyla dağıtılır. |

## Web arayüzü (wwwroot/lib)

| Bileşen | Lisans |
|---|---|
| Bootstrap | MIT |
| jQuery, jquery-validation, jquery-validation-unobtrusive | MIT |
| Chart.js 4 (`wwwroot/js/chart.js`) | MIT |
| Font Awesome Free | İkonlar CC BY 4.0, yazı tipleri SIL OFL 1.1, kod MIT (atıf zorunlu: bu dosya) |

## Yalnızca geliştirme / test (dağıtılmaz)

xUnit (Apache-2.0), NSubstitute (BSD-3-Clause), Microsoft.NET.Test.Sdk (MIT), Microsoft.AspNetCore.Mvc.Testing (MIT), Microsoft.EntityFrameworkCore.Sqlite (MIT).

## Yazı tipleri

PDF dışa aktarımı işletim sisteminde kurulu yazı tiplerini kullanır (Windows'ta Arial). Yazı tipi dosyası ürünle **dağıtılmaz**.
