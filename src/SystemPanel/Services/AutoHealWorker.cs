using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Services;

public class AutoHealWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AutoHealWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dpService = scope.ServiceProvider.GetRequiredService<IDpServiceStatusService>();
            var mail = scope.ServiceProvider.GetRequiredService<IMailService>();

            var settings = db.AutoHealSettings.FirstOrDefault();

            if (settings == null || !settings.Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            var states = db.AutoHealStates
                .Where(x => !x.Disabled)
                .ToList();

            foreach (var s in states)
            {
                if (s.LastHealUtc != null &&
                    DateTime.UtcNow - s.LastHealUtc <
                    TimeSpan.FromMinutes(settings.CooldownMinutes))
                    continue;

                try
                {
                    var ok = await dpService
                        .RestartCriticalServicesByProvince(s.ProvinceName);

                    db.AutoHealLogs.Add(new AutoHealLog
                    {
                        Province = s.ProvinceName,
                        Action = "AUTO_HEAL",
                        Success = ok
                    });

                    if (ok)
                        s.LastHealUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    db.AutoHealLogs.Add(new AutoHealLog
                    {
                        Province = s.ProvinceName,
                        Action = "AUTO_HEAL",
                        Success = false,
                        ErrorMessage = ex.Message
                    });

                    var subject = $"🚨 AUTO-HEAL FAILED – {s.ProvinceName}";
                    var body =
$"""
AUTO-HEAL BAŞARISIZ

Province : {s.ProvinceName}
Time     : {DateTime.UtcNow:u}
Error    : {ex.Message}

Bu mail Skype / SIEM alarm sistemine yönlendirilecektir.
""";

                    await mail.SendAlertAsync(subject, body);
                }
            }

            db.SaveChanges();

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
