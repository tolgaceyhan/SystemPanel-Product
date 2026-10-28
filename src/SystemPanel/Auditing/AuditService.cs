using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SystemPanel.Data;
using SystemPanel.Infrastructure;
using SystemPanel.Models;

namespace SystemPanel.Auditing
{
    /// <summary>Kaydedilecek denetim olayı. Before/After/Metadata nesneleri redaksiyondan geçer.</summary>
    public sealed record AuditRecord(string Action)
    {
        public string? ResourceType { get; init; }
        public string? ResourceId { get; init; }
        public string? ResourceName { get; init; }
        public string? Scope { get; init; }
        public bool Success { get; init; } = true;
        public object? Before { get; init; }
        public object? After { get; init; }
        public object? Metadata { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }

        /// <summary>Boşsa HttpContext'teki kullanıcıdan alınır.</summary>
        public string? ActorUserName { get; init; }
        public string? ActorUserId { get; init; }
        public string? ActorType { get; init; }
    }

    public interface IAuditService
    {
        /// <summary>
        /// Olayı kalıcı olarak kaydeder. Veritabanı yazımı başarısız olursa olay
        /// Critical seviyede uygulama loguna düşer; çağıranın işlemi kesilmez.
        /// </summary>
        Task RecordAsync(AuditRecord record, CancellationToken ct = default);
    }

    public sealed class AuditService : IAuditService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<AuditService> _logger;

        public AuditService(IServiceScopeFactory scopeFactory, IHttpContextAccessor http, ILogger<AuditService> logger)
        {
            _scopeFactory = scopeFactory;
            _http = http;
            _logger = logger;
        }

        public async Task RecordAsync(AuditRecord record, CancellationToken ct = default)
        {
            var entity = Build(record, _http.HttpContext);

            // Teknik loga da yapısal olarak düşer (SIEM'e iletilebilir). Hassas alan içermez.
            _logger.LogInformation(
                "AUDIT {Action} {Success} actor={Actor} resource={ResourceType}:{ResourceId} cid={CorrelationId}",
                entity.Action, entity.Success, entity.ActorUserName, entity.ResourceType, entity.ResourceId, entity.CorrelationId);

            try
            {
                // İsteğin DbContext'inden bağımsız: iş işlemi başarısız/rollback olsa bile audit yazılır.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.AuditEvents.Add(entity);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex,
                    "AUDIT YAZILAMADI {Action} actor={Actor} resource={ResourceType}:{ResourceId} success={Success} cid={CorrelationId}",
                    entity.Action, entity.ActorUserName, entity.ResourceType, entity.ResourceId, entity.Success, entity.CorrelationId);
            }
        }

        internal static AuditEvent Build(AuditRecord record, HttpContext? http)
        {
            var user = http?.User;
            bool authenticated = user?.Identity?.IsAuthenticated == true;

            return new AuditEvent
            {
                TimestampUtc = DateTime.UtcNow,
                ActorUserName = AuditRedactor.Truncate(record.ActorUserName ?? (authenticated ? user!.Identity!.Name : null), 256),
                ActorUserId = AuditRedactor.Truncate(record.ActorUserId ?? (authenticated ? user!.FindFirstValue(ClaimTypes.NameIdentifier) : null), 450),
                ActorType = record.ActorType ?? (authenticated ? "User" : http == null ? "System" : "Anonymous"),
                SourceIp = http?.Connection.RemoteIpAddress?.ToString(),
                Action = record.Action,
                ResourceType = AuditRedactor.Truncate(record.ResourceType, 64),
                ResourceId = AuditRedactor.Truncate(record.ResourceId, 450),
                ResourceName = AuditRedactor.Truncate(record.ResourceName, 256),
                Scope = AuditRedactor.Truncate(record.Scope, 1024),
                Success = record.Success,
                BeforeJson = AuditRedactor.ToRedactedJson(record.Before),
                AfterJson = AuditRedactor.ToRedactedJson(record.After),
                MetadataJson = AuditRedactor.ToRedactedJson(record.Metadata),
                CorrelationId = http != null ? CorrelationIdMiddleware.Get(http) : null,
                ErrorCode = AuditRedactor.Truncate(record.ErrorCode, 64),
                ErrorMessage = AuditRedactor.Truncate(record.ErrorMessage, 1024),
            };
        }
    }
}
