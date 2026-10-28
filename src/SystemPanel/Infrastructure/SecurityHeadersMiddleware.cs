using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SystemPanel.Infrastructure
{
    /// <summary>
    /// Temel güvenlik başlıkları. CSP bilinçli olarak dar tutuldu: görünümlerde satır içi
    /// script'ler olduğu için script-src kısıtlaması şimdilik yok (ROADMAP: CSP sıkılaştırma).
    /// frame-ancestors 'self' clickjacking'i önler; WPF Dashboard sayfaları iframe değil
    /// WebView2 üst düzey gezinme ile açtığı için etkilenmez.
    /// </summary>
    public sealed class SecurityHeadersMiddleware
    {
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
                h["Content-Security-Policy"] = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'; form-action 'self'";
                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
