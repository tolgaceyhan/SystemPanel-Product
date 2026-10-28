using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SystemPanel.ViewModels.Statistics;

namespace SystemPanel.Services
{
    public class StatisticsService : IStatisticsService
    {
        private readonly HealthCheckService _healthCheckService;
        private readonly UserManager<IdentityUser> _users;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StatisticsService> _logger;

        private const string CacheKey = "dashboard_statistics";

        public StatisticsService(
            HealthCheckService healthCheckService,
            UserManager<IdentityUser> users,
            IMemoryCache cache,
            ILogger<StatisticsService> logger)
        {
            _healthCheckService = healthCheckService;
            _users = users;
            _cache = cache;
            _logger = logger;
        }

        public async Task<StatisticsViewModel> GetStatisticsAsync()
        {
            if (_cache.TryGetValue(CacheKey, out StatisticsViewModel? cached) && cached != null)
                return cached;

            var report = await _healthCheckService.CheckHealthAsync();

            int? totalUsers = null;
            try
            {
                totalUsers = await _users.Users.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kullanıcı sayısı okunamadı.");
            }

            var model = new StatisticsViewModel
            {
                TotalUsers = totalUsers,
                ActiveSessions = null,
                SystemHealth = report.Status switch
                {
                    HealthStatus.Healthy => "Healthy",
                    HealthStatus.Degraded => "Degraded",
                    _ => "Unhealthy"
                },
            };

            _cache.Set(CacheKey, model, TimeSpan.FromSeconds(30));
            return model;
        }
    }
}
