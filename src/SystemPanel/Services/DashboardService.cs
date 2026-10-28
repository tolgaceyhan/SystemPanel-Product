using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using SystemPanel.Data;
using SystemPanel.Security.Scope;
using SystemPanel.ViewModels.Home;

namespace SystemPanel.Services
{
    public class DashboardService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IOrganizationalScopeResolver _scopes;

        public DashboardService(
            ApplicationDbContext db,
            IMemoryCache cache,
            IOrganizationalScopeResolver scopes)
        {
            _db = db;
            _cache = cache;
            _scopes = scopes;
        }

        public async Task<DashboardApiDto> GetDashboardAsync(ClaimsPrincipal user)
        {
            // Önceden: Admin değil VE il claim'i YOKSA filtre uygulanmıyor, tüm sunucular görünüyordu (fail-open).
            var scope = await _scopes.ResolveAsync(user);
            var province = user.FindFirst("Province")?.Value;

            var latestQuery = _db.LatestServerMetrics.AsNoTracking();

            if (!scope.IsGlobal)
            {
                latestQuery = string.IsNullOrEmpty(province)
                    ? latestQuery.Where(x => false)
                    : latestQuery.Where(x => x.Province == province);
            }

            var latest = await latestQuery.ToListAsync();
            var visibleServers = latest.Select(x => x.ServerName).ToList();

            return new DashboardApiDto
            {
                Metrics = new MetricsDto
                {
                    Cpu = latest.Select(x => new MetricPointDto
                    {
                        Time = x.LastUpdatedUtc.ToLocalTime().ToString("HH:mm"),
                        Value = x.CpuUsage
                    }).ToList(),

                    Ram = latest.Select(x => new MetricPointDto
                    {
                        Time = x.LastUpdatedUtc.ToLocalTime().ToString("HH:mm"),
                        Value = x.RamUsage
                    }).ToList()
                },

                DpServers = latest.Select(x => new DpServerDto
                {
                    ServerName = x.ServerName,
                    Cpu = x.CpuUsage,
                    Ram = x.RamUsage,
                    Status = (DateTime.UtcNow - x.LastUpdatedUtc).TotalMinutes <= 2
                        ? "Healthy"
                        : "Offline"
                }).ToList(),

                Alarms = await _db.AlarmLogs
                    .AsNoTracking()
                    .Where(x => scope.IsGlobal || visibleServers.Contains(x.ServerName))
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(5)
                    .Select(x => new AlarmDto
                    {
                        Server = x.ServerName,
                        Message = x.Message,
                        Time = x.CreatedAt.ToLocalTime().ToString("HH:mm")
                    }).ToListAsync()
            };
        }
    }
}
