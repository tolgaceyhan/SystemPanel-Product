using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SystemPanel.Infrastructure
{
    /// <summary>
    /// Temel güvenlik başlıkları. CSP tüm kaynakları uygulamanın kendisiyle sınırlar ('self'): kapalı
    /// ağda çalışma garantisi ve harici script yüklenmesine karşı koruma. Görünümlerde satır içi
    /// script/style olduğu için şimdilik 'unsafe-inline' açık (ROADMAP: nonce tabanlı CSP).
    /// frame-ancestors 'self' clickjacking'i önler; WPF Dashboard sayfaları iframe değil
    /// WebView2 üst düzey gezinme ile açtığı için etkilenmez.
    /// </summary>
    public sealed class SecurityHeadersMiddleware
    {
        public const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "frame-ancestors 'self'; object-src 'none'; base-uri 'self'; form-action 'self'";

        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                var h = context.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "SAMEORIGIN";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                h["Content-Security-Policy"] = ContentSecurityPolicy;
                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
