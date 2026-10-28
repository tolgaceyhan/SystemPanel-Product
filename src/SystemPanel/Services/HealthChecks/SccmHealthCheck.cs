using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

public sealed class SccmHealthCheck : IHealthCheck
{
    private readonly IConfiguration _cfg;
    public SccmHealthCheck(IConfiguration cfg) => _cfg = cfg;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
    {
        var server = _cfg["SCCM:SqlServer"];
        var db = _cfg["SCCM:Database"];
        return Task.FromResult(!string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(db)
            ? HealthCheckResult.Healthy("SCCM config OK")
            : HealthCheckResult.Degraded("SCCM config missing"));
    }
}
