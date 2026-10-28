using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed class WarmupHostedService : IHostedService
{
    private readonly HealthCheckService _hc;
    private readonly ILogger<WarmupHostedService> _log;
    public WarmupHostedService(HealthCheckService hc, ILogger<WarmupHostedService> log)
    { _hc = hc; _log = log; }

    public async Task StartAsync(CancellationToken ct)
    {
        var report = await _hc.CheckHealthAsync(_ => true, ct);
        foreach (var kv in report.Entries)
            _log.LogInformation("Health {Name} => {Status}", kv.Key, kv.Value.Status);

        if (report.Status != HealthStatus.Healthy)
            _log.LogWarning("Readiness NOT HEALTHY at startup: {Status}", report.Status);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
