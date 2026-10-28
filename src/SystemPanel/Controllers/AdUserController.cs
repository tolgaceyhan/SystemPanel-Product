using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemPanel.Models;
using SystemPanel.Security;
using SystemPanel.Services.ActiveDirectory;
using SystemPanel.ViewModels.AdUser;

namespace SystemPanel.Controllers
{
    /// <summary>
    /// AD kullanıcı işlemleri. İzin: [RequirePermission]; kapsam (il/birim), ayrıcalıklı hesap
    /// kuralları ve audit: <see cref="IAdUserOperations"/>. Önceki [OuScopeAuthorize] filtresi
    /// yalnızca "ou" adlı parametrelere bakıyordu; hedef kullanıcıyı taşıyan userDn/id kontrol edilmiyordu.
    /// </summary>
    [RequirePermission(Permissions.AdUsersView)]
    [Route("ad-users")]
    public class AdUserController : Controller
    {
        private readonly IAdUserOperations _ops;
        private readonly ILogger<AdUserController> _logger;

        public AdUserController(IAdUserOperations ops, ILogger<AdUserController> logger)
        {
            _ops = ops;
            _logger = logger;
        }

        // ============================================================
        // INDEX + SEARCH
        // ============================================================
        [HttpGet("")]
        public async Task<IActionResult> Index(string? term, string? ou, CancellationToken ct = default)
        {
            var users = await _ops.SearchAsync(User, term, ou, ct);
            return View(users);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(string term, CancellationToken ct = default)
        {
            var users = await _ops.SearchAsync(User, term, null, ct);
            return PartialView("Partials/_SearchResults", users);
        }

        // ============================================================
        // USER DETAILS (AJAX)
        // ============================================================
        [HttpGet("details")]
        public async Task<IActionResult> Details(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("id gerekli.");

            var user = await _ops.GetUserAsync(User, id, ct);
            return user == null ? NotFound() : Ok(user);
        }

        // ============================================================
        // OU BROWSER
        // ============================================================
        [HttpGet("ou-browser")]
        public IActionResult OuBrowser() => View();

        [HttpGet("ou-tree")]
        public async Task<IActionResult> OuTree(CancellationToken ct = default)
        {
            var tree = await _ops.GetOuTreeAsync(User, ct);
            return tree == null ? Ok(new OuTreeDto { OuName = "-", DistinguishedName = "" }) : Ok(tree);
        }

        // ============================================================
        // GROUP MANAGEMENT
        // ============================================================
        [HttpGet("user-groups")]
        public async Task<IActionResult> GetUserGroups(string dn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dn))
                return BadRequest("dn gerekli.");

            var (result, groups) = await _ops.GetUserGroupsAsync(User, dn, ct);
            if (!result.Success)
                return StatusCode(result.StatusCode, new { error = result.Error });

            return Ok(groups.Select(g => new { distinguishedName = g, name = g }));
        }

        [HttpGet("search-groups")]
        [RequirePermission(Permissions.AdGroupsManage)]
        public async Task<IActionResult> SearchGroups(string term, CancellationToken ct = default)
        {
            var groups = await _ops.SearchGroupsAsync(User, term, ct);
            return Ok(groups.Select(g => new
            {
                distinguishedName = g.DistinguishedName,
                name = g.Name,
                samAccountName = g.SamAccountName,
                description = g.Description
            }));
        }

        [HttpPost("add-to-group")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdGroupsManage)]
        public async Task<IActionResult> AddToGroup(string userDn, string groupDn, CancellationToken ct = default)
            => ToJson(await _ops.AddToGroupAsync(User, userDn, groupDn, ct));

        [HttpPost("remove-from-group")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdGroupsManage)]
        public async Task<IActionResult> RemoveFromGroup(string userDn, string groupDn, CancellationToken ct = default)
            => ToJson(await _ops.RemoveFromGroupAsync(User, userDn, groupDn, ct));

        // ============================================================
        // CREATE USER
        // ============================================================
        [HttpGet("create")]
        [RequirePermission(Permissions.AdUsersCreate)]
        public async Task<IActionResult> Create(CancellationToken ct = default)
            => View(new CreateAdUserViewModel { AvailableOus = await OuOptionsAsync(ct) });

        [HttpPost("create")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersCreate)]
        public async Task<IActionResult> Create(CreateAdUserViewModel vm, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                vm.AvailableOus = await OuOptionsAsync(ct);
                return View(vm);
            }

            var result = await _ops.CreateAsync(User, vm, ct);
            TempData[result.Success ? "Success" : "Error"] = result.Success ? "Kullanıcı oluşturuldu." : result.Error;
            return RedirectToAction("Index");
        }

        // ============================================================
        // EDIT USER
        // ============================================================
        [HttpGet("edit")]
        public async Task<IActionResult> Edit(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("id gerekli.");

            var u = await _ops.GetUserAsync(User, id, ct);
            if (u == null)
                return NotFound();

            return View(new EditAdUserViewModel
            {
                Id = u.DistinguishedName,
                DistinguishedName = u.DistinguishedName,
                SamAccountName = u.SamAccountName,
                UserPrincipalName = u.UserPrincipalName,
                DisplayName = u.DisplayName,
                GivenName = u.GivenName,
                Surname = u.Surname,
                Email = u.Email,
                Mail = u.Mail,
                MailNickname = u.MailNickname,
                Mobile = u.Mobile,
                TelephoneNumber = u.TelephoneNumber,
                Department = u.Department,
                Description = u.Description,
                Enabled = u.Enabled,
                IsLocked = u.IsLocked,
                OuPath = u.OuPath,
                WhenCreated = u.WhenCreated,
                WhenChanged = u.WhenChanged
            });
        }

        [HttpPost("edit")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersEdit)]
        public async Task<IActionResult> Edit(EditAdUserViewModel vm, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var result = await _ops.UpdateAsync(User, vm, ct);
            if (result.StatusCode == 404)
                return NotFound();

            TempData[result.Success ? "Success" : "Error"] = result.Success ? "Kullanıcı güncellendi." : result.Error;
            return RedirectToAction("Edit", new { id = vm.Id });
        }

        // ============================================================
        // PASSWORD MANAGEMENT
        // ============================================================
        [HttpPost("reset-password")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersPasswordReset)]
        public async Task<IActionResult> ResetPassword(string userDn, string newPassword, bool mustChange = false, CancellationToken ct = default)
            => ToJson(await _ops.ResetPasswordAsync(User, userDn, newPassword, unlock: false, mustChangeAtNextLogon: mustChange, ct));

        [HttpPost("reset-password-unlock")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersPasswordReset)]
        [RequirePermission(Permissions.AdUsersUnlock)]
        public async Task<IActionResult> ResetPasswordUnlock(string userDn, string newPassword, bool mustChange = false, CancellationToken ct = default)
            => ToJson(await _ops.ResetPasswordAsync(User, userDn, newPassword, unlock: true, mustChangeAtNextLogon: mustChange, ct));

        // ============================================================
        // ACCOUNT STATUS (ENABLE / DISABLE / UNLOCK)
        // ============================================================
        [HttpPost("disable")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersDisable)]
        public async Task<IActionResult> DisableUser(string userDn, CancellationToken ct = default)
            => ToJson(await _ops.SetEnabledAsync(User, userDn, enabled: false, ct));

        [HttpPost("enable")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersDisable)]
        public async Task<IActionResult> EnableUser(string userDn, CancellationToken ct = default)
            => ToJson(await _ops.SetEnabledAsync(User, userDn, enabled: true, ct));

        [HttpPost("unlock")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersUnlock)]
        public async Task<IActionResult> UnlockUser(string userDn, CancellationToken ct = default)
            => ToJson(await _ops.UnlockAsync(User, userDn, ct));

        // ============================================================
        // OU LIST (AJAX DROPDOWN) — yalnızca kapsam içindeki OU'lar
        // ============================================================
        [HttpGet("ou-list")]
        public async Task<IActionResult> OuList(CancellationToken ct = default)
        {
            var result = await _ops.GetAssignableOusAsync(User, ct);
            return Ok(result.Select(o => new { name = o.Name, distinguishedName = o.DistinguishedName }));
        }

        // ============================================================
        // MOVE USER TO OU
        // ============================================================
        [HttpPost("move")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AdUsersMove)]
        public async Task<IActionResult> Move(string userDn, string targetOuDn, CancellationToken ct = default)
            => ToJson(await _ops.MoveAsync(User, userDn, targetOuDn, ct));

        // ============================================================
        private async Task<System.Collections.Generic.IEnumerable<SelectListItem>> OuOptionsAsync(CancellationToken ct)
            => (await _ops.GetAssignableOusAsync(User, ct))
                .Select(o => new SelectListItem { Value = o.DistinguishedName, Text = o.Name })
                .ToList();

        /// <summary>Mevcut JS istemcileri { ok } bekler; hata durumunda HTTP durum kodu + error döner.</summary>
        private IActionResult ToJson(AdOperationResult result)
            => result.Success
                ? Ok(new { ok = true })
                : StatusCode(result.StatusCode, new { ok = false, error = result.Error });
    }
}
