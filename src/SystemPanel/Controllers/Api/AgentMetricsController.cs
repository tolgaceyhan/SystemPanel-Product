using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Controllers.Api;
using SystemPanel.Models.Dto.Ingest;
using SystemPanel.Services.Agents;

/// <summary>
/// ESKİ (anonim) ingest ucu — yalnızca sahadaki eski agent'ların yeni sürüme geçişi için.
/// Varsayılan olarak KAPALIDIR (Monitoring:AllowLegacyAnonymousIngest=false → 410 Gone).
/// Yeni agent'lar /api/v1/agents/metrics kullanır (kimlik doğrulamalı). Bkz. docs/ADR/001.
/// </summary>
[ApiController]
[Route("api/agentmetrics")]
[AllowAnonymous]
[EnableRateLimiting(AgentRateLimit.PolicyName)]
public class AgentMetricsController : ControllerBase
{
    private readonly IMetricsIngestionService _ingestion;
    private readonly MonitoringOptions _options;
    private readonly ILogger<AgentMetricsController> _logger;

    public AgentMetricsController(IMetricsIngestionService ingestion, IOptions<MonitoringOptions> options, ILogger<AgentMetricsController> logger)
    {
        _ingestion = ingestion;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("ingest")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Ingest([FromBody] AgentEnvelopeDto dto, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!_options.AllowLegacyAnonymousIngest)
        {
            _logger.LogWarning("Eski anonim ingest isteği reddedildi (kapalı). Host={Host} Ip={Ip}", dto?.Hostname, ip);
            return Problem(statusCode: StatusCodes.Status410Gone,
                title: "Bu uç kapatıldı. Agent'ı güncelleyip /api/v1/agents/metrics ile kimlik doğrulamalı gönderin.");
        }

        _logger.LogWarning("KİMLİK DOĞRULAMASIZ eski ingest kabul edildi (geçiş modu). Host={Host} Ip={Ip}", dto?.Hostname, ip);

        MetricSample sample;
        try
        {
            sample = FromLegacy(dto!, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or NullReferenceException)
        {
            return Problem(statusCode: 400, title: "Geçersiz istek.");
        }

        await _ingestion.IngestAsync(sample, ip, ct);
        return Ok(new { message = "ok" });
    }

    /// <summary>Eski zarf biçimini ({Hostname, CollectedAtUtc, Metrics:[{Type, Data}]}) doğrulanmış örneğe çevirir.</summary>
    internal static MetricSample FromLegacy(AgentEnvelopeDto dto, DateTime nowUtc)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Hostname) || dto.Metrics == null || dto.Metrics.Count == 0)
            throw new ArgumentException("Eksik alan.");

        int? cpu = null, ram = null;
        string? province = null, ou = null;
        var disks = new List<(string, int)>();

        foreach (var m in dto.Metrics)
        {
            switch (m.Type?.ToLowerInvariant())
            {
                case "cpu" when m.Data.TryGetProperty("usage", out var c):
                    cpu = MetricsIngestionService.Percent(c.GetDouble()) ?? throw new ArgumentException("cpu");
                    break;
                case "ram" when m.Data.TryGetProperty("usage", out var r):
                    ram = MetricsIngestionService.Percent(r.GetDouble()) ?? throw new ArgumentException("ram");
                    break;
                case "disk" when m.Data.TryGetProperty("drives", out var drives) && drives.ValueKind == JsonValueKind.Array:
                    foreach (var d in drives.EnumerateArray().Take(64))
                    {
                        if (!d.TryGetProperty("name", out var n) || !d.TryGetProperty("usage", out var u)) continue;
                        var name = (n.GetString() ?? "").Trim();
                        var usage = MetricsIngestionService.Percent(u.GetDouble());
                        if (name.Length is > 0 and <= 32 && usage != null) disks.Add((name, usage.Value));
                    }
                    break;
                case "identity":
                    // Eski davranış korunur (yalnızca geçiş modunda): il/OU agent'ın bildirdiği değerdir.
                    if (m.Data.TryGetProperty("province", out var p)) province = p.GetString();
                    if (m.Data.TryGetProperty("ou", out var o)) ou = o.GetString();
                    break;
            }
        }

        return new MetricSample(
            MetricsIngestionService.ValidateHostname(dto.Hostname),
            MetricsIngestionService.SaneTimestamp(dto.CollectedAtUtc, nowUtc),
            cpu, ram, disks,
            string.IsNullOrWhiteSpace(province) ? null : province.Trim()[..Math.Min(province.Trim().Length, 100)],
            string.IsNullOrWhiteSpace(ou) ? null : ou.Trim()[..Math.Min(ou.Trim().Length, 450)]);
    }
}
