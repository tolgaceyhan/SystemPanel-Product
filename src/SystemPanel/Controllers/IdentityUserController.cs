using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using SystemPanel.Auditing;
using SystemPanel.Security;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.UsersManage)]
    public class IdentityUserController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _audit;

        public IdentityUserController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IAuditService audit)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> AssignRole(string userName)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
                return NotFound();

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRole(string userName, string role)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
                return NotFound();

            // Önceden var olmayan rol adı girildiğinde rol OTOMATİK oluşturuluyordu (yazım hatası = yeni rol).
            if (string.IsNullOrWhiteSpace(role) || !await _roleManager.RoleExistsAsync(role))
            {
                ViewBag.Status = "Geçersiz rol.";
                return View(user);
            }

            var result = await _userManager.AddToRoleAsync(user, role);

            await _audit.RecordAsync(new AuditRecord(AuditActions.IdentityUserRoleAssign)
            {
                ResourceType = "IdentityUser",
                ResourceId = user.Id,
                ResourceName = user.UserName,
                Success = result.Succeeded,
                After = new { Role = role },
            });

            ViewBag.Status = result.Succeeded
                ? $"{userName} kullanıcısına '{role}' rolü atandı."
                : "Rol atanamadı.";
            return View(user);
        }
    }
}
