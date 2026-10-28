using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SystemPanel.Data;

namespace SystemPanel.Security
{
    public interface IRolePermissionStore
    {
        /// <summary>Rol adı → izinler. Anahtarlar büyük/küçük harf duyarsızdır.</summary>
        Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetMapAsync(CancellationToken ct = default);

        void Invalidate();
    }

    public interface IPermissionService
    {
        Task<IReadOnlySet<string>> GetPermissionsAsync(ClaimsPrincipal user, CancellationToken ct = default);
        Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default);
    }

    /// <summary>
    /// RolePermissions tablosundan eşlemeyi okur (60 sn önbellek). Tablo boşsa veya
    /// veritabanına ulaşılamıyorsa (ör. migration henüz uygulanmadıysa)
    /// <see cref="DefaultRolePermissions"/> kullanılır.
    /// </summary>
    public sealed class DbRolePermissionStore : IRolePermissionStore
    {
        private const string CacheKey = "security:role-permission-map";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<DbRolePermissionStore> _logger;

        public DbRolePermissionStore(IServiceScopeFactory scopeFactory, IMemoryCache cache, ILogger<DbRolePermissionStore> logger)
        {
            _scopeFactory = scopeFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetMapAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, IReadOnlyCollection<string>>? cached) && cached != null)
                return cached;

            IReadOnlyDictionary<string, IReadOnlyCollection<string>> map;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var rows = await db.RolePermissions.AsNoTracking()
                    .Select(r => new { r.RoleName, r.Permission })
                    .ToListAsync(ct);

                map = rows.Count == 0
                    ? DefaultRolePermissions.Map
                    : rows.GroupBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(
                              g => g.Key,
                              g => (IReadOnlyCollection<string>)g.Select(r => r.Permission).Distinct().ToArray(),
                              StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Rol-izin eşlemesi veritabanından okunamadı; varsayılan eşleme kullanılıyor.");
                map = DefaultRolePermissions.Map;
            }

            _cache.Set(CacheKey, map, CacheDuration);
            return map;
        }

        public void Invalidate() => _cache.Remove(CacheKey);
    }

    public sealed class PermissionService : IPermissionService
    {
        private readonly IRolePermissionStore _store;

        public PermissionService(IRolePermissionStore store) => _store = store;

        public async Task<IReadOnlySet<string>> GetPermissionsAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (user?.Identity?.IsAuthenticated != true)
                return result;

            var map = await _store.GetMapAsync(ct);

            foreach (var role in user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (map.TryGetValue(role, out var permissions))
                    result.UnionWith(permissions);
            }

            return result;
        }

        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default)
            => (await GetPermissionsAsync(user, ct)).Contains(permission);
    }
}
