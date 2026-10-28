using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SystemPanel.Data
{
    /// <summary>
    /// Açılışta <see cref="SeedData.InitializeAsync"/> çalıştırır: eksik Identity rollerini oluşturur ve
    /// yalnızca Bootstrap:AdminUserName/AdminPassword verilmişse ilk yerel yöneticiyi ekler.
    /// (SeedData önceden hiçbir yerden çağrılmıyordu.) Veritabanına ulaşılamazsa uygulama açılmaya devam eder.
    /// </summary>
    public sealed class IdentityBootstrapper : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IdentityBootstrapper> _logger;

        public IdentityBootstrapper(IServiceScopeFactory scopeFactory, ILogger<IdentityBootstrapper> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await SeedData.InitializeAsync(scope.ServiceProvider);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Identity rolleri / ilk yönetici oluşturulamadı.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
