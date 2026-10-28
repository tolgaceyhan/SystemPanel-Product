using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SystemPanel.Auditing;
using SystemPanel.Models;
using SystemPanel.Security.Scope;
using SystemPanel.ViewModels.AdUser;

namespace SystemPanel.Services.ActiveDirectory
{
    public sealed record AdOperationResult(bool Success, int StatusCode, string? Error)
    {
        public static AdOperationResult Ok() => new(true, 200, null);
        public static AdOperationResult NotFound() => new(false, 404, "Nesne bulunamadı.");
        public static AdOperationResult Denied(string reason) => new(false, 403, reason);
        public static AdOperationResult Invalid(string reason) => new(false, 400, reason);
        public static AdOperationResult Failed(string reason) => new(false, 502, reason);
    }

    /// <summary>
    /// AD kullanıcı işlemleri için TEK giriş noktası. Her işlemde:
    /// (1) hedef nesne AD'den okunarak kanonik DN'i bulunur (kullanıcının gönderdiği DN dizesine güvenilmez),
    /// (2) işlemi yapanın organizasyonel kapsamı kontrol edilir,
    /// (3) ayrıcalıklı hesaplar / korumalı gruplar için ek kural uygulanır,
    /// (4) sonuç (başarılı, başarısız, reddedildi) audit'e yazılır.
    /// İzin (permission) kontrolü controller'daki [RequirePermission] ile yapılır.
    /// </summary>
    public interface IAdUserOperations
    {
        Task<AdUser?> GetUserAsync(ClaimsPrincipal actor, string idOrDn, CancellationToken ct = default);
        Task<List<AdUser>> SearchAsync(ClaimsPrincipal actor, string? term, string? ou, CancellationToken ct = default);
        Task<List<OrganizationalUnit>> GetAssignableOusAsync(ClaimsPrincipal actor, CancellationToken ct = default);
        Task<OuTreeDto?> GetOuTreeAsync(ClaimsPrincipal actor, CancellationToken ct = default);
        Task<List<AdGroup>> SearchGroupsAsync(ClaimsPrincipal actor, string? term, CancellationToken ct = default);
        Task<(AdOperationResult Result, List<string> Groups)> GetUserGroupsAsync(ClaimsPrincipal actor, string userDn, CancellationToken ct = default);

        Task<AdOperationResult> ResetPasswordAsync(ClaimsPrincipal actor, string userDn, string newPassword, bool unlock, bool mustChangeAtNextLogon, CancellationToken ct = default);
        Task<AdOperationResult> SetEnabledAsync(ClaimsPrincipal actor, string userDn, bool enabled, CancellationToken ct = default);
        Task<AdOperationResult> UnlockAsync(ClaimsPrincipal actor, string userDn, CancellationToken ct = default);
        Task<AdOperationResult> UpdateAsync(ClaimsPrincipal actor, EditAdUserViewModel model, CancellationToken ct = default);
        Task<AdOperationResult> MoveAsync(ClaimsPrincipal actor, string userDn, string targetOuDn, CancellationToken ct = default);
        Task<AdOperationResult> CreateAsync(ClaimsPrincipal actor, CreateAdUserViewModel model, CancellationToken ct = default);
        Task<AdOperationResult> AddToGroupAsync(ClaimsPrincipal actor, string userDn, string groupDn, CancellationToken ct = default);
        Task<AdOperationResult> RemoveFromGroupAsync(ClaimsPrincipal actor, string userDn, string groupDn, CancellationToken ct = default);
    }

    public sealed class AdUserOperations : IAdUserOperations
    {
        private const string ResourceType = "AdUser";

        private readonly IAdUserService _ad;
        private readonly IOrganizationalScopeResolver _scopes;
        private readonly IAuditService _audit;
        private readonly ILogger<AdUserOperations> _logger;

        public AdUserOperations(IAdUserService ad, IOrganizationalScopeResolver scopes, IAuditService audit, ILogger<AdUserOperations> logger)
        {
            _ad = ad;
            _scopes = scopes;
            _audit = audit;
            _logger = logger;
        }

        // ======================= OKUMA =======================

        public async Task<AdUser?> GetUserAsync(ClaimsPrincipal actor, string idOrDn, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            var user = await LoadAsync(idOrDn);
            if (user == null)
                return null;

            if (!scope.Contains(user.DistinguishedName))
            {
                // Kapsam dışı nesne "yok" gibi davranır (varlığı sızdırılmaz) ama deneme audit'lenir.
                await DeniedAsync("ad.user.read", user, scope, "Hedef kullanıcı kapsam dışında.");
                return null;
            }

            return user;
        }

        public async Task<List<AdUser>> SearchAsync(ClaimsPrincipal actor, string? term, string? ou, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            term = (term ?? string.Empty).Trim();

            if (scope.IsEmpty)
                return new List<AdUser>();

            IEnumerable<string> bases;
            if (!string.IsNullOrWhiteSpace(ou))
            {
                var baseDn = _ad.BuildOuDn(ou);
                if (!scope.Contains(baseDn))
                    return new List<AdUser>();
                bases = new[] { baseDn };
            }
            else if (scope.IsGlobal)
            {
                // Tüm domain'in filtresiz listelenmesini engelle: arama terimi zorunlu.
                if (term.Length == 0)
                    return new List<AdUser>();
                bases = new[] { _ad.ResolveBaseDn() };
            }
            else
            {
                bases = scope.Roots.Select(r => r.ToString());
            }

            var results = new List<AdUser>();
            foreach (var b in bases)
                results.AddRange(await _ad.GetAdUsersInOuAsync(term, b, ct));

            // Savunma derinliği: dönen her kaydı yeniden kapsam kontrolünden geçir.
            return results
                .Where(u => scope.Contains(u.DistinguishedName))
                .GroupBy(u => u.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        public async Task<List<OrganizationalUnit>> GetAssignableOusAsync(ClaimsPrincipal actor, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            if (scope.IsEmpty)
                return new List<OrganizationalUnit>();

            var all = await _ad.GetOrganizationalUnitsByNameAsync(string.Empty, ct);
            return all.Where(o => scope.Contains(o.DistinguishedName)).ToList();
        }

        public async Task<OuTreeDto?> GetOuTreeAsync(ClaimsPrincipal actor, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            if (scope.IsEmpty)
                return null;

            var tree = await _ad.GetOuTreeAsync(ct);
            return scope.IsGlobal ? tree : PruneToScope(tree, scope);
        }

        /// <summary>Global olmayan kullanıcı yalnızca kendi kapsam köklerinin alt ağaçlarını görür.</summary>
        internal static OuTreeDto PruneToScope(OuTreeDto tree, OrganizationalScope scope)
        {
            var visible = new List<OuTreeDto>();

            void Walk(OuTreeDto node)
            {
                if (scope.Contains(node.DistinguishedName))
                {
                    visible.Add(node);
                    return; // kökün tamamı (alt dallarıyla) görünür
                }
                foreach (var child in node.Children)
                    Walk(child);
            }

            Walk(tree);
            return new OuTreeDto { OuName = tree.OuName, DistinguishedName = tree.DistinguishedName, Children = visible };
        }

        public async Task<List<AdGroup>> SearchGroupsAsync(ClaimsPrincipal actor, string? term, CancellationToken ct = default)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            if (scope.IsEmpty)
                return new List<AdGroup>();

            var groups = await _ad.GetAllSecurityGroupsAsync(term ?? string.Empty);
            return groups
                .Where(g => !_ad.IsProtectedGroup(g.DistinguishedName))
                .Where(g => scope.Contains(g.DistinguishedName))
                .ToList();
        }

        public async Task<(AdOperationResult Result, List<string> Groups)> GetUserGroupsAsync(ClaimsPrincipal actor, string userDn, CancellationToken ct = default)
        {
            var (user, scope, denied) = await AuthorizeTargetAsync(actor, userDn, "ad.user.groups.read", requireNonPrivileged: false, ct);
            if (denied != null)
                return (denied, new List<string>());

            return (AdOperationResult.Ok(), await _ad.GetUserGroupsAsync(user!.DistinguishedName));
        }

        // ======================= DEĞİŞTİREN =======================

        public async Task<AdOperationResult> ResetPasswordAsync(ClaimsPrincipal actor, string userDn, string newPassword, bool unlock, bool mustChangeAtNextLogon, CancellationToken ct = default)
        {
            const string action = AuditActions.AdUserPasswordReset;
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword.Length > 256)
                return AdOperationResult.Invalid("Parola en az 8 karakter olmalıdır.");

            var (user, scope, denied) = await AuthorizeTargetAsync(actor, userDn, action, requireNonPrivileged: true, ct);
            if (denied != null) return denied;

            bool ok = unlock
                ? await _ad.ResetPasswordAndUnlockAsync(user!.DistinguishedName, newPassword)
                : await _ad.ResetPasswordManualAsync(user!.DistinguishedName, newPassword);

            if (ok && mustChangeAtNextLogon)
            {
                try { await _ad.ForceChangeAtNextLogon(user.DistinguishedName); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Parola sıfırlandı fakat 'sonraki girişte değiştir' ayarlanamadı.");
                    ok = false;
                }
            }

            // Parola ASLA audit'e yazılmaz; yalnızca işlem seçenekleri.
            return await CompleteAsync(action, user, scope, ok, metadata: new { Unlock = unlock, MustChangeAtNextLogon = mustChangeAtNextLogon });
        }

        public async Task<AdOperationResult> SetEnabledAsync(ClaimsPrincipal actor, string userDn, bool enabled, CancellationToken ct = default)
        {
            var action = enabled ? AuditActions.AdUserEnable : AuditActions.AdUserDisable;
            var (user, scope, denied) = await AuthorizeTargetAsync(actor, userDn, action, requireNonPrivileged: true, ct);
            if (denied != null) return denied;

            if (!enabled && IsSelf(actor, user!))
                return await DeniedAsync(action, user, scope, "Kullanıcı kendi hesabını devre dışı bırakamaz.");

            bool ok = enabled ? await _ad.EnableUserAsync(user!.DistinguishedName) : await _ad.DisableUserAsync(user!.DistinguishedName);
            return await CompleteAsync(action, user, scope, ok, before: new { user.Enabled }, after: new { Enabled = enabled });
        }

        public async Task<AdOperationResult> UnlockAsync(ClaimsPrincipal actor, string userDn, CancellationToken ct = default)
        {
            const string action = AuditActions.AdUserUnlock;
            var (user, scope, denied) = await AuthorizeTargetAsync(actor, userDn, action, requireNonPrivileged: true, ct);
            if (denied != null) return denied;

            bool ok = await _ad.UnlockAccountAsync(user!.DistinguishedName);
            return await CompleteAsync(action, user, scope, ok, before: new { user.IsLocked });
        }

        public async Task<AdOperationResult> UpdateAsync(ClaimsPrincipal actor, EditAdUserViewModel model, CancellationToken ct = default)
        {
            const string action = AuditActions.AdUserUpdate;
            var (user, scope, denied) = await AuthorizeTargetAsync(actor, model.Id, action, requireNonPrivileged: true, ct);
            if (denied != null) return denied;

            var before = EditableFields(user!);
            var after = new Dictionary<string, string?>
            {
                ["GivenName"] = model.GivenName,
                ["Surname"] = model.Surname,
                ["DisplayName"] = model.DisplayName,
                ["Mail"] = model.Mail,
                ["MailNickname"] = model.MailNickname,
                ["UserPrincipalName"] = model.UserPrincipalName,
                ["Mobile"] = model.Mobile,
                ["TelephoneNumber"] = model.TelephoneNumber,
                ["Department"] = model.Department,
                ["Description"] = model.Description,
            };

            bool ok = await _ad.UpdateUserAsync(user!.DistinguishedName, model);

            var changed = after.Keys.Where(k => !string.Equals(before.GetValueOrDefault(k) ?? "", after[k] ?? "", StringComparison.Ordinal)).ToList();
            return await CompleteAsync(action, user, scope, ok,
                before: changed.ToDictionary(k => k, k => before.GetValueOrDefault(k)),
                after: changed.ToDictionary(k => k, k => after[k]));
        }

        public async Task<AdOperationResult> MoveAsync(ClaimsPrincipal actor, string userDn, string targetOuDn, CancellationToken ct = default)
        {
            const string action = AuditActions.AdUserMove;
            var (user, scope, denied) = await AuthorizeTargetAsync(actor, userDn, action, requireNonPrivileged: true, ct);
            if (denied != null) return denied;

            if (!DistinguishedName.TryParse(targetOuDn, out var target) || target.Rdns[0].Type != "OU")
                return AdOperationResult.Invalid("Geçersiz hedef OU.");

            // Hem kaynak hem hedef kapsam içinde olmalı (önceden kaynak kullanıcı hiç kontrol edilmiyordu:
            // bir LocalAdmin domain'deki herhangi bir kullanıcıyı kendi OU'suna çekebiliyordu).
            if (!scope.Contains(target))
                return await DeniedAsync(action, user, scope, "Hedef OU kapsam dışında.", new { TargetOu = target.ToString() });

            var fromOu = DistinguishedName.Parse(user!.DistinguishedName).Parent?.ToString();
            bool ok = await _ad.MoveUserToOuAsync(user.DistinguishedName, target.ToString());
            return await CompleteAsync(action, user, scope, ok, before: new { Ou = fromOu }, after: new { Ou = target.ToString() });
        }

        public async Task<AdOperationResult> CreateAsync(ClaimsPrincipal actor, CreateAdUserViewModel model, CancellationToken ct = default)
        {
            const string action = AuditActions.AdUserCreate;
            var scope = await _scopes.ResolveAsync(actor, ct);

            if (!DistinguishedName.TryParse(model.SelectedOu, out var ou) || ou.Rdns[0].Type != "OU")
                return AdOperationResult.Invalid("Geçersiz hedef OU.");

            if (!scope.Contains(ou))
            {
                await RecordAsync(action, ou.ToString(), model.UserName, scope, false, "denied", "Hedef OU kapsam dışında.");
                return AdOperationResult.Denied("Hedef OU kapsam dışında.");
            }

            var user = new AdUser
            {
                UserName = model.UserName,
                SamAccountName = model.UserName,
                DisplayName = model.DisplayName,
                DistinguishedName = ou.ToString(),
                Mail = model.Email,
            };

            bool ok = await _ad.CreateUserAsync(user, model.Password);
            await RecordAsync(action, $"CN={DistinguishedName.Rdn.Escape(model.DisplayName ?? model.UserName)},{ou}", model.UserName, scope, ok,
                ok ? null : "failed", ok ? null : "Kullanıcı oluşturulamadı.",
                after: new { model.UserName, model.DisplayName, model.Email, Ou = ou.ToString() });

            return ok ? AdOperationResult.Ok() : AdOperationResult.Failed("Kullanıcı oluşturulamadı.");
        }

        public Task<AdOperationResult> AddToGroupAsync(ClaimsPrincipal actor, string userDn, string groupDn, CancellationToken ct = default)
            => ChangeGroupAsync(actor, userDn, groupDn, add: true, ct);

        public Task<AdOperationResult> RemoveFromGroupAsync(ClaimsPrincipal actor, string userDn, string groupDn, CancellationToken ct = default)
            => ChangeGroupAsync(actor, userDn, groupDn, add: false, ct);

        private async Task<AdOperationResult> ChangeGroupAsync(ClaimsPrincipal actor, string userDn, string groupDn, bool add, CancellationToken ct)
        {
            var action = add ? AuditActions.AdGroupMemberAdd : AuditActions.AdGroupMemberRemove;
            var (user, scope, denied) = await AuthorizeTargetAsync(actor, userDn, action, requireNonPrivileged: true, ct);
            if (denied != null) return denied;

            if (!DistinguishedName.TryParse(groupDn, out var group) || group.Rdns[0].Type != "CN")
                return AdOperationResult.Invalid("Geçersiz grup.");

            if (_ad.IsProtectedGroup(group.ToString()))
                return await DeniedAsync(action, user, scope, "Ayrıcalıklı / korumalı grubun üyeliği panelden değiştirilemez.", new { Group = group.ToString() });

            // Önceki sürüm izin verilen grupları işlemi YAPANA göre değil HEDEF kullanıcıya göre hesaplıyordu.
            if (!scope.Contains(group))
                return await DeniedAsync(action, user, scope, "Grup kapsam dışında.", new { Group = group.ToString() });

            bool ok = add
                ? await _ad.AddUserToGroupByDn(user!.DistinguishedName, group.ToString())
                : await _ad.RemoveUserFromGroupByDn(user!.DistinguishedName, group.ToString());

            return await CompleteAsync(action, user, scope, ok, metadata: new { Group = group.ToString() });
        }

        // ======================= YARDIMCILAR =======================

        private async Task<AdUser?> LoadAsync(string? idOrDn)
        {
            if (string.IsNullOrWhiteSpace(idOrDn) || idOrDn.Length > 2048)
                return null;

            var user = await _ad.GetUserByIdAsync(idOrDn.Trim());
            return user == null || string.IsNullOrWhiteSpace(user.DistinguishedName) ? null : user;
        }

        private async Task<(AdUser? User, OrganizationalScope Scope, AdOperationResult? Denied)> AuthorizeTargetAsync(
            ClaimsPrincipal actor, string? userDn, string action, bool requireNonPrivileged, CancellationToken ct)
        {
            var scope = await _scopes.ResolveAsync(actor, ct);
            var user = await LoadAsync(userDn);
            if (user == null)
                return (null, scope, AdOperationResult.NotFound());

            if (!scope.Contains(user.DistinguishedName))
                return (user, scope, await DeniedAsync(action, user, scope, "Hedef kullanıcı kapsam dışında."));

            if (requireNonPrivileged && user.IsPrivileged && !scope.IsGlobal)
                return (user, scope, await DeniedAsync(action, user, scope, "Ayrıcalıklı (adminCount=1) hesaplar yalnızca organizasyon geneli yetkiyle değiştirilebilir."));

            return (user, scope, null);
        }

        private static bool IsSelf(ClaimsPrincipal actor, AdUser user)
            => string.Equals(actor.FindFirst(ScopeClaimTypes.AdDn)?.Value, user.DistinguishedName, StringComparison.OrdinalIgnoreCase);

        private static Dictionary<string, string?> EditableFields(AdUser u) => new()
        {
            ["GivenName"] = u.GivenName,
            ["Surname"] = u.Surname,
            ["DisplayName"] = u.DisplayName,
            ["Mail"] = u.Mail,
            ["MailNickname"] = u.MailNickname,
            ["UserPrincipalName"] = u.UserPrincipalName,
            ["Mobile"] = u.Mobile,
            ["TelephoneNumber"] = u.TelephoneNumber,
            ["Department"] = u.Department,
            ["Description"] = u.Description,
        };

        private async Task<AdOperationResult> CompleteAsync(string action, AdUser? user, OrganizationalScope scope, bool ok,
            object? before = null, object? after = null, object? metadata = null)
        {
            await RecordAsync(action, user?.DistinguishedName, user?.SamAccountName, scope, ok,
                ok ? null : "failed", ok ? null : "AD işlemi başarısız.", before, after, metadata);
            return ok ? AdOperationResult.Ok() : AdOperationResult.Failed("AD işlemi başarısız. Ayrıntı uygulama logunda.");
        }

        private async Task<AdOperationResult> DeniedAsync(string action, AdUser? user, OrganizationalScope scope, string reason, object? metadata = null)
        {
            _logger.LogWarning("AD işlemi reddedildi: {Action} hedef={Target} neden={Reason}", action, user?.DistinguishedName, reason);
            await RecordAsync(action, user?.DistinguishedName, user?.SamAccountName, scope, false, "denied", reason, metadata: metadata);
            return AdOperationResult.Denied(reason);
        }

        private Task RecordAsync(string action, string? dn, string? name, OrganizationalScope scope, bool success,
            string? errorCode, string? error, object? before = null, object? after = null, object? metadata = null)
            => _audit.RecordAsync(new AuditRecord(action)
            {
                ResourceType = ResourceType,
                ResourceId = dn,
                ResourceName = name,
                Scope = scope.Describe(),
                Success = success,
                ErrorCode = errorCode,
                ErrorMessage = error,
                Before = before,
                After = after,
                Metadata = metadata,
            });
    }
}
