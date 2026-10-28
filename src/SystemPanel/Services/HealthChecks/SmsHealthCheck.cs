using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

public sealed class SmsHealthCheck : IHealthCheck
{
    private readonly IConfiguration _cfg;
    public SmsHealthCheck(IConfiguration cfg) => _cfg = cfg;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
    {
        var url = _cfg["SMS:ApiUrl"];
        var key = _cfg["SMS:ApiKey"] ?? _cfg["SMS:ApiKeySecretName"]; // senin şemana uy
        return Task.FromResult(!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key)
            ? HealthCheckResult.Healthy("SMS config OK")
            : HealthCheckResult.Degraded("SMS config missing"));
    }
}
