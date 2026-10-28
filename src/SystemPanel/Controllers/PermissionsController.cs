using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemPanel.Auditing;
using SystemPanel.Data;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.ViewModels;

namespace SystemPanel.Controllers
{
    /// <summary>
    /// Rol → izin matrisi. Önceden statik demo veri gösteriyordu; artık RolePermissions
    /// tablosunu (yoksa varsayılan eşlemeyi) gösterir ve düzenler.
    /// </summary>
    [RequirePermission(Permissions.SettingsView)]
    public class PermissionsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IRolePermissionStore _store;
        private readonly RoleManager<IdentityRole> _roles;
        private readonly IAuditService _audit;

        public PermissionsController(ApplicationDbContext db, IRolePermissionStore store, RoleManager<IdentityRole> roles, IAuditService audit)
        {
            _db = db;
            _store = store;
            _roles = roles;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var map = await _store.GetMapAsync();

            var roleNames = map.Keys
                .Concat(await _roles.Roles.Select(r => r.Name!).ToListAsync())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r)
                .ToList();

            return View(new RolePermissionMatrixViewModel
            {
                Roles = roleNames,
                Permissions = Permissions.All.ToList(),
                Granted = map.SelectMany(kv => kv.Value.Select(p => Key(kv.Key, p))).ToHashSet(StringComparer.OrdinalIgnoreCase),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.SettingsManage)]
        public async Task<IActionResult> Index(List<string>? granted)
        {
            var requested = (granted ?? new List<string>())
                .Select(Parse)
                .Where(x => x != null && Permissions.IsKnown(x.Value.Permission))
                .Select(x => x!.Value)
                .Where(x => !string.Equals(x.Role, DefaultRolePermissions.Admin, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            // Admin rolü tanımı gereği tüm izinlere sahiptir (matristen kısıtlanamaz).
            requested.AddRange(Permissions.All.Select(p => (DefaultRolePermissions.Admin, p)));

            // Kendini kilitleme koruması: en az bir rol ayarları yönetebilmelidir.
            if (!requested.Any(x => x.Permission == Permissions.SettingsManage))
            {
                TempData["Error"] = "En az bir rolde 'settings.manage' izni kalmalıdır.";
                return RedirectToAction(nameof(Index));
            }

            var before = (await _store.GetMapAsync())
                .SelectMany(kv => kv.Value.Select(p => Key(kv.Key, p)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var after = requested.Select(x => Key(x.Role, x.Permission)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                _db.RolePermissions.RemoveRange(await _db.RolePermissions.ToListAsync());
                _db.RolePermissions.AddRange(requested.Select(x => new RolePermission { RoleName = x.Role, Permission = x.Permission }));
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            _store.Invalidate();

            await _audit.RecordAsync(new AuditRecord(AuditActions.RolePermissionsUpdate)
            {
                ResourceType = "RolePermissions",
                Before = new { Removed = before.Except(after, StringComparer.OrdinalIgnoreCase).OrderBy(x => x) },
                After = new { Added = after.Except(before, StringComparer.OrdinalIgnoreCase).OrderBy(x => x) },
            });

            TempData["Success"] = "Rol-izin eşlemesi kaydedildi. Değişiklik en geç 60 saniye içinde geçerli olur.";
            return RedirectToAction(nameof(Index));
        }

        internal static string Key(string role, string permission) => $"{role}|{permission}";

        private static (string Role, string Permission)? Parse(string value)
        {
            var parts = (value ?? string.Empty).Split('|', 2);
            return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && parts[0].Length <= 256
                ? (parts[0].Trim(), parts[1].Trim())
                : null;
        }
    }
}
