using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Auditing;
using SystemPanel.Contracts.Agents;
using SystemPanel.Data;

namespace SystemPanel.Services.Agents
{
    public static class AgentAuthentication
    {
        public const string Scheme = "Agent";
        public const string AgentIdClaim = "agent:id";
        public const string HostnameClaim = "agent:hostname";
        public const string ProvinceClaim = "agent:province";
    }

    /// <summary>
    /// X-Agent-Id + X-Agent-Key başlıklarıyla agent kimlik doğrulaması. Anahtar DB'deki SHA-256 özetiyle
    /// sabit zamanlı karşılaştırılır. Başarısız denemeler loglanır ve (IP başına dakikada bir) audit'lenir.
    /// </summary>
    public sealed class AgentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _audit;
        private readonly IMemoryCache _cache;

        public AgentAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ApplicationDbContext db,
            IAuditService audit,
            IMemoryCache cache)
            : base(options, logger, encoder)
        {
            _db = db;
            _audit = audit;
            _cache = cache;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var idHeader = Request.Headers[AgentApi.AgentIdHeader].FirstOrDefault();
            var key = Request.Headers[AgentApi.AgentKeyHeader].FirstOrDefault();

            if (string.IsNullOrEmpty(idHeader) && string.IsNullOrEmpty(key))
                return AuthenticateResult.NoResult();

            if (!Guid.TryParse(idHeader, out var agentId) || string.IsNullOrEmpty(key))
                return await FailAsync("malformed", null);

            var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId);
            if (agent == null || !AgentKeys.Verify(key, agent.KeyHash))
                return await FailAsync("invalid_credentials", agentId);

            if (!agent.IsEnabled)
                return await FailAsync("disabled", agentId);

            var claims = new[]
            {
                new Claim(AgentAuthentication.AgentIdClaim, agent.Id.ToString()),
                new Claim(AgentAuthentication.HostnameClaim, agent.Hostname),
                new Claim(AgentAuthentication.ProvinceClaim, agent.Province ?? string.Empty),
                new Claim(ClaimTypes.Name, "agent:" + agent.Hostname),
            };

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AgentAuthentication.Scheme));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, AgentAuthentication.Scheme));
        }

        private async Task<AuthenticateResult> FailAsync(string reason, Guid? agentId)
        {
            var ip = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            Logger.LogWarning("Agent kimlik doğrulaması başarısız: {Reason} agent={AgentId} ip={Ip}", reason, agentId, ip);

            // Audit tablosunu taşırmamak için aynı IP+neden için dakikada bir kayıt.
            var throttleKey = $"agent-auth-fail:{ip}:{reason}";
            if (!_cache.TryGetValue(throttleKey, out _))
            {
                _cache.Set(throttleKey, true, TimeSpan.FromMinutes(1));
                await _audit.RecordAsync(new AuditRecord(AuditActions.AgentAuthFailed)
                {
                    ActorType = "Agent",
                    ActorUserName = agentId?.ToString(),
                    ResourceType = "Agent",
                    ResourceId = agentId?.ToString(),
                    Success = false,
                    ErrorCode = reason,
                });
            }

            return AuthenticateResult.Fail("Agent kimlik doğrulaması başarısız.");
        }
    }
}
