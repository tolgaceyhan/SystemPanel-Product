using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using SystemPanel.Auditing;
using SystemPanel.Security;
using SystemPanel.Services;
using SystemPanel.ViewModels.UserManagement;

namespace SystemPanel.Controllers
{
    [RequirePermission(Permissions.UsersManage)]
    public class UserManagementController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _audit;

        public UserManagementController(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService audit)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _audit = audit;
        }

        // =========================================================
        // LIST / INDEX
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string sort = "UserName",
            string dir = "asc",
            int page = 1,
            int pageSize = 20)
        {
            var query = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(u =>
                    u.UserName.Contains(search) ||
                    u.Email.Contains(search));
            }

            // sort
            bool asc = dir?.ToLower() != "desc";
            query = sort switch
            {
                "Email" => asc ? query.OrderBy(u => u.Email) : query.OrderByDescending(u => u.Email),
                _ => asc ? query.OrderBy(u => u.UserName) : query.OrderByDescending(u => u.UserName)
            };

            var totalItems = query.Count();
            if (page < 1) page = 1;

            var users = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var vm = new UserListViewModel
            {
                Search = search ?? "",
                Sort = sort,
                Dir = asc ? "asc" : "desc",
                CurrentPage = page,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);

                vm.Users.Add(new UserListItemViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    EmailConfirmed = u.EmailConfirmed,
                    LockoutEnabled = u.LockoutEnabled,
                    LockoutEnd = u.LockoutEnd,
                    Roles = roles.ToList()
                });
            }

            return View("Index", vm);
        }

        // =========================================================
        // DETAILS
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest();

            var u = await _userManager.FindByIdAsync(id);
            if (u == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(u);

            var vm = new UserListItemViewModel
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                EmailConfirmed = u.EmailConfirmed,
                LockoutEnabled = u.LockoutEnabled,
                LockoutEnd = u.LockoutEnd,
                Roles = roles.ToList()
            };

            return View("UserDetails", vm);
        }

        // =========================================================
        // CREATE
        // =========================================================
        [HttpGet]
        public IActionResult Create()
        {
            return View("CreateUser", new CreateUserViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel vm)
        {
            if (!ModelState.IsValid)
                return View("CreateUser", vm);

            var user = new IdentityUser
            {
                UserName = vm.UserName,
                Email = vm.Email,
                PhoneNumber = vm.PhoneNumber
            };

            var result = await _userManager.CreateAsync(user, vm.Password);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                return View("CreateUser", vm);
            }

            if (!string.IsNullOrWhiteSpace(vm.Role) && await _roleManager.RoleExistsAsync(vm.Role))
                await _userManager.AddToRoleAsync(user, vm.Role);

            await _audit.RecordAsync(new AuditRecord(AuditActions.IdentityUserCreate)
            {
                ResourceType = "IdentityUser",
                ResourceId = user.Id,
                ResourceName = user.UserName,
                After = new { user.UserName, user.Email, user.PhoneNumber, vm.Role },
            });

            TempData["Success"] = "Kullanıcı başarıyla oluşturuldu.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // EDIT
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest();

            var u = await _userManager.FindByIdAsync(id);
            if (u == null) return NotFound();

            var allRoles = _roleManager.Roles.Select(r => r.Name).ToList();
            var userRoles = await _userManager.GetRolesAsync(u);

            var vm = new EditUserViewModel
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                LockoutEnabled = u.LockoutEnabled,
                IsLockedOut = u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow,
                AllRoles = allRoles,
                Roles = userRoles.ToList(),
                SelectedRoles = userRoles.ToList()
            };

            return View("EditUser", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditUserViewModel vm)
        {
            if (!ModelState.IsValid)
                return View("EditUser", vm);

            var u = await _userManager.FindByIdAsync(vm.Id);
            if (u == null) return NotFound();

            u.Email = vm.Email;
            u.PhoneNumber = vm.PhoneNumber;
            u.LockoutEnabled = vm.LockoutEnabled;

            var updateResult = await _userManager.UpdateAsync(u);
            if (!updateResult.Succeeded)
            {
                foreach (var e in updateResult.Errors)
                    ModelState.AddModelError(string.Empty, e.Description);
                return View("EditUser", vm);
            }

            // Roller
            var currentRoles = await _userManager.GetRolesAsync(u);
            var selected = vm.SelectedRoles ?? new System.Collections.Generic.List<string>();

            var toRemove = currentRoles.Where(r => !selected.Contains(r)).ToList();
            var toAdd = selected.Where(r => !currentRoles.Contains(r)).ToList();

            if (toRemove.Any())
                await _userManager.RemoveFromRolesAsync(u, toRemove);

            if (toAdd.Any())
                await _userManager.AddToRolesAsync(u, toAdd);

            await _audit.RecordAsync(new AuditRecord(AuditActions.IdentityUserUpdate)
            {
                ResourceType = "IdentityUser",
                ResourceId = u.Id,
                ResourceName = u.UserName,
                Before = new { Roles = currentRoles },
                After = new { u.Email, u.PhoneNumber, u.LockoutEnabled, Roles = selected },
            });

            TempData["Success"] = "Kullanıcı güncellendi.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DELETE
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest();

            var u = await _userManager.FindByIdAsync(id);
            if (u == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(u);

            var vm = new UserListItemViewModel
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Roles = roles.ToList()
            };

            return View("DeleteUser", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var u = await _userManager.FindByIdAsync(id);
            if (u == null) return NotFound();

            var result = await _userManager.DeleteAsync(u);

            await _audit.RecordAsync(new AuditRecord(AuditActions.IdentityUserDelete)
            {
                ResourceType = "IdentityUser",
                ResourceId = u.Id,
                ResourceName = u.UserName,
                Success = result.Succeeded,
            });
            if (!result.Succeeded)
            {
                TempData["Error"] = "Kullanıcı silinemedi.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Kullanıcı silindi.";
            return RedirectToAction(nameof(Index));
        }

        // NOT: Önceki ResetAdPassword (stub nedeniyle değişmemiş parolayı SMS ile gönderiyordu) ve
        // ReadLaps (audit'siz LAPS okuma) uçları kaldırıldı; hiçbir ekran kullanmıyordu. Aynı işlemler
        // AD Kullanıcılar (ad-users/reset-password) ve AD Bilgisayarlar (ad-computers/laps) ekranlarında
        // izin + kapsam + audit kontrolüyle yapılır.
    }
}
