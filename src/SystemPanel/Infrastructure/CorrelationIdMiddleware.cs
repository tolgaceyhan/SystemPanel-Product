using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace SystemPanel.Infrastructure
{
    /// <summary>
    /// Her isteğe bir correlation ID atar (gelen <c>X-Correlation-ID</c> geçerliyse onu kullanır),
    /// yanıt başlığına yazar, Serilog LogContext'e ekler ve HttpContext.TraceIdentifier yapar.
    /// Kullanıcıya gösterilen hata sayfası ve audit kayıtları bu ID'yi taşır.
    /// </summary>
    public sealed class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-ID";
        private const string ItemKey = "SystemPanel.CorrelationId";

        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
            var id = IsValid(incoming) ? incoming! : Guid.NewGuid().ToString("N");

            context.Items[ItemKey] = id;
            context.TraceIdentifier = id;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = id;
                return Task.CompletedTask;
            });

            using (LogContext.PushProperty("CorrelationId", id))
            {
                await _next(context);
            }
        }

        public static string Get(HttpContext context)
            => context.Items.TryGetValue(ItemKey, out var v) && v is string s ? s : context.TraceIdentifier;

        /// <summary>Log enjeksiyonunu önlemek için yalnızca kısa alfanümerik ID'ler kabul edilir.</summary>
        internal static bool IsValid(string? value)
            => !string.IsNullOrEmpty(value)
               && value.Length <= 64
               && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    }
}
