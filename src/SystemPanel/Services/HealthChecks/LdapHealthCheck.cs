using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace SystemPanel.Services.HealthChecks
{
    public sealed class LdapHealthCheck : IHealthCheck
    {
        private readonly LdapProbeService _probe;
        public LdapHealthCheck(LdapProbeService probe) => _probe = probe;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
            => await _probe.PingAsync(ct)
               ? HealthCheckResult.Healthy("LDAP bind OK")
               : HealthCheckResult.Unhealthy("LDAP bind FAILED");
    }
}
