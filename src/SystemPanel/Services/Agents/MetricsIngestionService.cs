using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Contracts.Agents;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Services.Agents
{
    /// <summary>Doğrulanmış, normalize edilmiş tek bir ölçüm.</summary>
    public sealed record MetricSample(
        string Hostname,
        DateTime CollectedAtUtc,
        int? CpuPercent,
        int? RamPercent,
        IReadOnlyList<(string Name, int UsagePercent)> Disks,
        string? Province = null,
        string? Ou = null);

    public interface IMetricsIngestionService
    {
        Task IngestAsync(MetricSample sample, string? sourceIp, CancellationToken ct = default);
    }

    /// <summary>
    /// Agent metriklerini mevcut tablolara (ServerInfos, LatestServerMetrics, ServerMetricHistory,
    /// ServerDiskHistory, AlarmLogs) yazar. Tablo yapısı ve anlamları Sprint 0 öncesiyle aynıdır;
    /// mantık AgentMetricsController içinden buraya taşındı ve doğrulama eklendi.
    /// </summary>
    public sealed class MetricsIngestionService : IMetricsIngestionService
    {
        private static readonly Regex DriveName = new(@"^[A-Za-z0-9:\\/ _\-\.]{1,32}$", RegexOptions.Compiled);
        private static readonly Regex Hostname = new(@"^[A-Za-z0-9][A-Za-z0-9\-]{0,62}$", RegexOptions.Compiled);

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly MonitoringOptions _options;
        private readonly ILogger<MetricsIngestionService> _logger;

        public MetricsIngestionService(ApplicationDbContext db, IMemoryCache cache, IOptions<MonitoringOptions> options, ILogger<MetricsIngestionService> logger)
        {
            _db = db;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>v1 sözleşmesinden doğrulanmış örnek üretir. Geçersiz istekte <see cref="ArgumentException"/>.</summary>
        public static MetricSample FromContract(string hostname, AgentMetricsRequest request, DateTime nowUtc, string? province)
        {
            if (request == null) throw new ArgumentException("Boş istek.");
            if (request.Disks.Count > AgentApi.MaxDisks) throw new ArgumentException("Çok fazla disk.");

            var disks = request.Disks
                .Where(d => d != null)
                .Select(d => (Name: (d.Name ?? string.Empty).Trim(), Usage: Percent(d.UsagePercent)))
                .ToList();

            if (disks.Any(d => !DriveName.IsMatch(d.Name) || d.Usage == null))
                throw new ArgumentException("Geçersiz disk adı veya değeri.");

            int? cpu = request.Cpu == null ? null : Percent(request.Cpu.UsagePercent) ?? throw new ArgumentException("Geçersiz CPU değeri.");
            int? ram = request.Memory == null ? null : Percent(request.Memory.UsagePercent) ?? throw new ArgumentException("Geçersiz RAM değeri.");

            return new MetricSample(
                ValidateHostname(hostname),
                SaneTimestamp(request.CollectedAtUtc, nowUtc),
                cpu,
                ram,
                disks.Select(d => (d.Name, d.Usage!.Value)).ToList(),
                province);
        }

        internal static int? Percent(double value)
            => double.IsFinite(value) && value >= 0 && value <= 100 ? (int)Math.Round(value) : null;

        /// <summary>Saat sapması olan agent'ların geçmişi/geleceği bozmasını engeller.</summary>
        internal static DateTime SaneTimestamp(DateTime collectedAtUtc, DateTime nowUtc)
        {
            var t = collectedAtUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(collectedAtUtc, DateTimeKind.Utc) : collectedAtUtc.ToUniversalTime();
            return t > nowUtc.AddMinutes(5) || t < nowUtc.AddDays(-1) ? nowUtc : t;
        }

        internal static string ValidateHostname(string hostname)
        {
            var h = AgentKeys.NormalizeHostname(hostname);
            if (!Hostname.IsMatch(h)) throw new ArgumentException("Geçersiz sunucu adı.");
            return h;
        }

        public async Task IngestAsync(MetricSample sample, string? sourceIp, CancellationToken ct = default)
        {
            var serverName = sample.Hostname;

            var server = await _db.ServerInfos.FirstOrDefaultAsync(s => s.Name == serverName, ct);
            if (server == null)
            {
                server = new ServerInfo
                {
                    Name = serverName,
                    IsActive = true,
                    Type = "Server",
                    Description = "Auto registered by agent",
                    CreatedAt = DateTime.UtcNow,
                    IpAddress = sourceIp ?? string.Empty,
                    Location = sample.Province ?? "Auto",
                };
                _db.ServerInfos.Add(server);
            }

            server.LastSeenAt = sample.CollectedAtUtc;
            if (!string.IsNullOrWhiteSpace(sample.Province))
                server.Location = sample.Province;

            var latest = await _db.LatestServerMetrics.FirstOrDefaultAsync(x => x.ServerName == serverName, ct);
            if (latest == null)
            {
                latest = new LatestServerMetrics { ServerName = serverName };
                _db.LatestServerMetrics.Add(latest);
            }

            if (!string.IsNullOrWhiteSpace(sample.Province)) latest.Province = sample.Province;
            if (!string.IsNullOrWhiteSpace(sample.Ou)) latest.Ou = sample.Ou;

            var thresholds = _options.Thresholds;

            if (sample.CpuPercent is int cpu)
            {
                latest.CpuUsage = cpu;
                AddHistory(serverName, "cpu", cpu, null, sample.CollectedAtUtc);
                if (cpu > thresholds.CpuPercent) CreateAlarm(serverName, "CPU", cpu);
            }

            if (sample.RamPercent is int ram)
            {
                latest.RamUsage = ram;
                AddHistory(serverName, "ram", ram, null, sample.CollectedAtUtc);
                if (ram > thresholds.RamPercent) CreateAlarm(serverName, "RAM", ram);
            }

            if (sample.Disks.Count > 0)
            {
                foreach (var (name, usage) in sample.Disks)
                {
                    _db.ServerDiskHistory.Add(new ServerDiskHistory
                    {
                        ServerName = serverName,
                        DriveName = name,
                        Usage = usage,
                        CreatedAt = sample.CollectedAtUtc,
                    });
                    AddHistory(serverName, "disk", usage, name, sample.CollectedAtUtc);
                    if (usage > thresholds.DiskPercent) CreateAlarm(serverName, $"Disk {name}", usage);
                }

                latest.DiskUsage = sample.Disks.Max(d => d.UsagePercent);
            }

            latest.LastUpdatedUtc = sample.CollectedAtUtc;
            await _db.SaveChangesAsync(ct);
        }

        private void AddHistory(string server, string type, int value, string? extra, DateTime at)
            => _db.ServerMetricHistory.Add(new ServerMetricHistory
            {
                ServerName = server,
                MetricType = type,
                MetricValue = value,
                Extra = extra,
                CreatedAt = at,
            });

        private void CreateAlarm(string server, string type, int value)
        {
            var cacheKey = $"alarm:{server}:{type}";
            if (_cache.TryGetValue(cacheKey, out _)) return;

            _cache.Set(cacheKey, true, TimeSpan.FromMinutes(Math.Max(1, _options.Thresholds.CooldownMinutes)));

            _db.AlarmLogs.Add(new AlarmLog
            {
                ServerName = server,
                AlarmType = type,
                Message = $"{type} threshold exceeded: {value}%",
                CreatedAt = DateTime.UtcNow,
            });

            _logger.LogWarning("Alarm {Server} {Type} {Value}%", server, type, value);
        }
    }
}
