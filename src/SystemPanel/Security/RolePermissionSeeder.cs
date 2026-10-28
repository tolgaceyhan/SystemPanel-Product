using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemPanel.Data;
using SystemPanel.Models;

namespace SystemPanel.Security
{
    /// <summary>
    /// RolePermissions tablosu BOŞSA varsayılan eşlemeyi yazar. Mevcut kayıtlara dokunmaz.
    /// Veritabanına ulaşılamazsa (ör. migration uygulanmadıysa) uygulama açılmaya devam eder;
    /// <see cref="DbRolePermissionStore"/> varsayılan eşlemeye düşer.
    /// </summary>
    public sealed class RolePermissionSeeder : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RolePermissionSeeder> _logger;

        public RolePermissionSeeder(IServiceScopeFactory scopeFactory, ILogger<RolePermissionSeeder> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                if (await db.RolePermissions.AnyAsync(cancellationToken))
                {
                    await EnsureAdminHasAllPermissionsAsync(db, cancellationToken);
                    return;
                }

                foreach (var (role, permissions) in DefaultRolePermissions.Map)
                {
                    foreach (var permission in permissions)
                        db.RolePermissions.Add(new RolePermission { RoleName = role, Permission = permission });
                }

                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Varsayılan rol-izin eşlemesi yazıldı ({Count} rol).", DefaultRolePermissions.Map.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Rol-izin eşlemesi tohumlanamadı; varsayılan eşleme bellekte kullanılacak.");
            }
        }

        /// <summary>
        /// Admin rolü tanımı gereği tüm izinlere sahiptir. Sürüm yükseltmesinde eklenen yeni izinler
        /// (ör. automation.*) mevcut kurulumlarda Admin'e eklenir. Diğer rollere dokunulmaz.
        /// </summary>
        private async Task EnsureAdminHasAllPermissionsAsync(ApplicationDbContext db, CancellationToken ct)
        {
            var existing = await db.RolePermissions
                .Where(r => r.RoleName == DefaultRolePermissions.Admin)
                .Select(r => r.Permission)
                .ToListAsync(ct);

            var missing = Permissions.All.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count == 0)
                return;

            foreach (var permission in missing)
                db.RolePermissions.Add(new RolePermission { RoleName = DefaultRolePermissions.Admin, Permission = permission });

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Admin rolüne yeni izinler eklendi: {Permissions}", string.Join(", ", missing));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
