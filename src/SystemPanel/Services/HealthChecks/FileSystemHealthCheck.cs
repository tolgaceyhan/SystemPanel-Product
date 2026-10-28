using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SystemPanel.Services.HealthChecks
{
    public sealed class FileSystemHealthCheck : IHealthCheck
    {
        private readonly string _path;
        public FileSystemHealthCheck(IConfiguration cfg)
            => _path = cfg["FileStorage:Root"] ?? "wwwroot/uploads";

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(_path);
                var test = Path.Combine(_path, ".writable.check");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return Task.FromResult(HealthCheckResult.Healthy("FS OK"));
            }
            catch (System.Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"FS FAILED ({_path})", ex));
            }
        }
    }
}
