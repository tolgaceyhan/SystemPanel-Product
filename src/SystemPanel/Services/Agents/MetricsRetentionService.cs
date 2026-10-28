using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Auditing;
using SystemPanel.Data;

namespace SystemPanel.Services.Agents
{
    /// <summary>
    /// Metrik geçmişi ve alarm kayıtlarının saklama süresini uygular. Monitoring:Retention:Enabled=false
    /// (varsayılan) iken HİÇBİR ŞEY SİLMEZ. Silmeler küçük partiler halinde yapılır ve özetlenerek audit'lenir.
    /// </summary>
    public sealed class MetricsRetentionService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<MonitoringOptions> _options;
        private readonly ILogger<MetricsRetentionService> _logger;

        public MetricsRetentionService(IServiceScopeFactory scopeFactory, IOptionsMonitor<MonitoringOptions> options, ILogger<MetricsRetentionService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Uygulama açılışını yavaşlatmamak için kısa gecikme.
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var retention = _options.CurrentValue.Retention;
                if (retention.Enabled)
                {
                    try
                    {
                        await RunOnceAsync(retention, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Metrik retention çalıştırılamadı.");
                    }
                }

                await Task.Delay(TimeSpan.FromHours(Math.Clamp(retention.RunEveryHours, 1, 168)), stoppingToken);
            }
        }

        private async Task RunOnceAsync(RetentionOptions retention, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var metricCutoff = DateTime.UtcNow.AddDays(-Math.Max(7, retention.MetricHistoryDays));
            var alarmCutoff = DateTime.UtcNow.AddDays(-Math.Max(30, retention.AlarmDays));
            var batch = Math.Clamp(retention.BatchSize, 100, 50_000);

            int metrics = await DeleteInBatches(() => db.ServerMetricHistory.Where(x => x.CreatedAt < metricCutoff).OrderBy(x => x.Id).Take(batch).ExecuteDeleteAsync(ct), ct);
            int disks = await DeleteInBatches(() => db.ServerDiskHistory.Where(x => x.CreatedAt < metricCutoff).OrderBy(x => x.Id).Take(batch).ExecuteDeleteAsync(ct), ct);
            int alarms = await DeleteInBatches(() => db.AlarmLogs.Where(x => x.CreatedAt < alarmCutoff).OrderBy(x => x.Id).Take(batch).ExecuteDeleteAsync(ct), ct);

            if (metrics + disks + alarms > 0)
            {
                _logger.LogInformation("Retention: {Metrics} metrik, {Disks} disk, {Alarms} alarm kaydı silindi.", metrics, disks, alarms);
                await audit.RecordAsync(new AuditRecord("monitoring.retention")
                {
                    ActorType = "System",
                    ActorUserName = "system",
                    ResourceType = "MetricHistory",
                    Metadata = new { MetricCutoffUtc = metricCutoff, AlarmCutoffUtc = alarmCutoff, Metrics = metrics, Disks = disks, Alarms = alarms },
                }, ct);
            }
        }

        private static async Task<int> DeleteInBatches(Func<Task<int>> deleteBatch, CancellationToken ct)
        {
            int total = 0;
            while (!ct.IsCancellationRequested)
            {
                var deleted = await deleteBatch();
                total += deleted;
                if (deleted == 0) break;
                await Task.Delay(200, ct); // SQL Server'ı yormamak için
            }
            return total;
        }
    }
}
