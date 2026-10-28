using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SystemPanel.Automation
{
    /// <summary>
    /// Onaylanmış runbook taleplerini sırayla çalıştırır ve süresi dolan onay taleplerini kapatır.
    /// Aynı anda tek iş çalışır (MVP). BackgroundJobs:Automation:Enabled=false ile kapatılır.
    /// </summary>
    public sealed class AutomationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly AutomationOptions _options;
        private readonly ILogger<AutomationWorker> _logger;
        private readonly AutomationAlarmQueue _alarms;

        public AutomationWorker(IServiceScopeFactory scopes, IOptions<AutomationOptions> options, ILogger<AutomationWorker> logger, AutomationAlarmQueue alarms)
        {
            _scopes = scopes;
            _alarms = alarms;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RunInScopeAsync(async (s, _) =>
            {
                var interrupted = await s.FailInterruptedAsync(stoppingToken);
                if (interrupted > 0)
                    _logger.LogWarning("{Count} runbook çalıştırması yarıda kalmıştı; Failed olarak işaretlendi.", interrupted);
            });

            var delay = TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 1, 300));
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunInScopeAsync(async (s, schedules) =>
                {
                    // Zamanlamalar ve alarm tetikleyicileri (ADR-002 §5) çalıştırma oluşturur; çalıştırmalar aşağıda işlenir.
                    await schedules.FireDueAsync(stoppingToken);
                    while (_alarms.TryRead(out var alarm) && alarm != null)
                        await schedules.OnAlarmAsync(alarm, stoppingToken);

                    await s.ExpirePendingAsync(stoppingToken);
                    await s.ExpireOverdueDispatchesAsync(stoppingToken);
                    while (!stoppingToken.IsCancellationRequested && await s.ExecuteNextAsync(stoppingToken)) { }
                });

                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunInScopeAsync(Func<RunbookRunService, RunbookScheduleService, Task> action)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await action(scope.ServiceProvider.GetRequiredService<RunbookRunService>(), scope.ServiceProvider.GetRequiredService<RunbookScheduleService>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Otomasyon çalıştırıcısında hata.");
            }
        }
    }

    /// <summary>Açılışta yerleşik katalog klasöründeki imzalı paketleri içe aktarır.</summary>
    public sealed class RunbookCatalogInitializer : IHostedService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly AutomationOptions _options;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<RunbookCatalogInitializer> _logger;

        public RunbookCatalogInitializer(IServiceScopeFactory scopes, IOptions<AutomationOptions> options, IWebHostEnvironment env, ILogger<RunbookCatalogInitializer> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _env = env;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var directory = string.IsNullOrWhiteSpace(_options.CatalogDirectory)
                ? Path.Combine(_env.ContentRootPath, "runbooks")
                : Path.GetFullPath(_options.CatalogDirectory, _env.ContentRootPath);
            try
            {
                using var scope = _scopes.CreateScope();
                var imported = await scope.ServiceProvider.GetRequiredService<RunbookCatalog>().ImportDirectoryAsync(directory, cancellationToken);
                if (imported > 0)
                    _logger.LogInformation("{Count} runbook paketi katalogdan içe aktarıldı.", imported);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Runbook kataloğu içe aktarılamadı: {Directory}", directory);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
