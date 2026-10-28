using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace SystemPanel.Services.HealthChecks
{
    public sealed class SqlServerHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _cfg;
        public SqlServerHealthCheck(IConfiguration cfg) => _cfg = cfg;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
        {
            try
            {
                var cs = _cfg.GetConnectionString("DefaultConnection");
                using var cn = new SqlConnection(cs);
                await cn.OpenAsync(ct);
                using var cmd = new SqlCommand("SELECT 1", cn);
                await cmd.ExecuteScalarAsync(ct);
                return HealthCheckResult.Healthy("DB OK");
            }
            catch (System.Exception ex)
            {
                return HealthCheckResult.Unhealthy("DB FAILED", ex);
            }
        }
    }
}
