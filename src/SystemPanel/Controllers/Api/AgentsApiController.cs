using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Agents;
using SystemPanel.Data;
using SystemPanel.Services.Agents;

namespace SystemPanel.Controllers.Api
{
    /// <summary>
    /// Agent API v1. Yalnızca kayıtlı ve etkin agent'lar (X-Agent-Id + X-Agent-Key) erişebilir.
    /// Agent yalnızca KENDİ sunucusu adına veri gönderebilir; il bilgisi agent'tan değil kayıttan gelir.
    /// </summary>
    [ApiController]
    [Authorize(AuthenticationSchemes = AgentAuthentication.Scheme)]
    [EnableRateLimiting(AgentRateLimit.PolicyName)]
    public sealed class AgentsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IMetricsIngestionService _ingestion;
        private readonly MonitoringOptions _options;

        public AgentsApiController(ApplicationDbContext db, IMetricsIngestionService ingestion, IOptions<MonitoringOptions> options)
        {
            _db = db;
            _ingestion = ingestion;
            _options = options.Value;
        }

        [HttpPost("/" + AgentApi.HeartbeatPath)]
        public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeatRequest request, CancellationToken ct)
        {
            var agent = await CurrentAgentAsync(ct);
            if (agent == null)
                return Unauthorized();

            if (!HostnameMatches(agent.Hostname, request.Hostname))
                return Problem(statusCode: 403, title: "Agent yalnızca kayıtlı olduğu sunucu adına bildirim yapabilir.");

            agent.LastSeenUtc = DateTime.UtcNow;
            agent.LastIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            agent.AgentVersion = Trim(request.AgentVersion, 32);
            agent.OsName = Trim(request.OsName, 128);
            agent.OsVersion = Trim(request.OsVersion, 64);
            agent.Fqdn = Trim(request.Fqdn, 256);

            var server = await _db.ServerInfos.FirstOrDefaultAsync(s => s.Name == agent.Hostname, ct);
            if (server != null)
                server.LastSeenAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return Ok(new AgentHeartbeatResponse
            {
                ServerTimeUtc = DateTime.UtcNow,
                RecommendedIntervalSeconds = Math.Clamp(_options.RecommendedIntervalSeconds, 10, 3600),
            });
        }

        [HttpPost("/" + AgentApi.MetricsPath)]
        [RequestSizeLimit(256 * 1024)]
        public async Task<IActionResult> Metrics([FromBody] AgentMetricsRequest request, CancellationToken ct)
        {
            var agent = await CurrentAgentAsync(ct);
            if (agent == null)
                return Unauthorized();

            MetricSample sample;
            try
            {
                sample = MetricsIngestionService.FromContract(agent.Hostname, request, DateTime.UtcNow, agent.Province);
            }
            catch (ArgumentException ex)
            {
                return Problem(statusCode: 400, title: ex.Message);
            }

            agent.LastSeenUtc = DateTime.UtcNow;
            agent.LastIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _db.SaveChangesAsync(ct);

            await _ingestion.IngestAsync(sample, agent.LastIpAddress, ct);
            return Accepted();
        }

        private async Task<Models.Agent?> CurrentAgentAsync(CancellationToken ct)
        {
            var id = User.FindFirst(AgentAuthentication.AgentIdClaim)?.Value;
            return Guid.TryParse(id, out var agentId)
                ? await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId && a.IsEnabled, ct)
                : null;
        }

        internal static bool HostnameMatches(string registered, string? reported)
            => !string.IsNullOrWhiteSpace(reported) &&
               string.Equals(AgentKeys.NormalizeHostname(reported), registered, StringComparison.OrdinalIgnoreCase);

        private static string? Trim(string? v, int max)
            => string.IsNullOrWhiteSpace(v) ? null : (v.Length > max ? v.Substring(0, max) : v.Trim());
    }

    public static class AgentRateLimit
    {
        public const string PolicyName = "agents";
    }
}
