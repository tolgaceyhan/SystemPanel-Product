using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Threading.Tasks;
using SystemPanel.Auditing;
using SystemPanel.Configuration;

namespace SystemPanel.Services
{
    /// <summary>
    /// Önce AD (Kerberos/NTLM ile kimlik bilgisi doğrulama), sonra yerel Identity hesabı.
    /// AD kullanıcısının panel rolleri her girişte AD gruplarından (RoleMapping) yeniden hesaplanır.
    /// Hiçbir rol eşleşmezse giriş REDDEDİLİR (önceden var olmayan "User" rolü atanmaya çalışılıyor
    /// ve exception mesajı kullanıcıya gösteriliyordu).
    /// </summary>
    public class LoginService
    {
        private const string GenericFailure = "Kullanıcı adı veya parola hatalı.";

        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _audit;
        private readonly ILogger<LoginService> _logger;
        private readonly ActiveDirectoryOptions _ad;
        private readonly RoleMappingOptions _roleMap;

        public LoginService(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService audit,
            ILogger<LoginService> logger,
            IOptions<ActiveDirectoryOptions> ad,
            IOptions<RoleMappingOptions> roleMap)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _roleManager = roleManager;
            _audit = audit;
            _logger = logger;
            _ad = ad.Value;
            _roleMap = roleMap.Value;
        }

        public async Task<(bool ok, string message)> LoginAsync(ViewModels.Account.LoginViewModel model)
        {
            var userName = (model.UserName ?? string.Empty).Trim();

            try
            {
                if (!string.IsNullOrWhiteSpace(_ad.Domain))
                {
                    var adResult = await TryActiveDirectoryLoginAsync(userName, model.Password, model.RememberMe);
                    if (adResult.HasValue)
                        return adResult.Value;
                }

                // Yerel (SQL / Identity) hesap. Başarısız denemeler hesabı kilitler.
                var sqlSignIn = await _signInManager.PasswordSignInAsync(userName, model.Password, model.RememberMe, lockoutOnFailure: true);

                if (sqlSignIn.Succeeded)
                {
                    await AuditLoginAsync(userName, true, "local");
                    return (true, "Giriş başarılı.");
                }

                await AuditLoginAsync(userName, false, "local", sqlSignIn.IsLockedOut ? "locked_out" : "invalid_credentials");
                return (false, sqlSignIn.IsLockedOut
                    ? "Çok sayıda hatalı deneme nedeniyle hesap geçici olarak kilitlendi."
                    : GenericFailure);
            }
            catch (Exception ex)
            {
                // Altyapı ayrıntısı kullanıcıya gösterilmez; correlation ID ile logda bulunur.
                _logger.LogError(ex, "Giriş sırasında hata. User={User}", userName);
                await AuditLoginAsync(userName, false, "error", ex.GetType().Name);
                return (false, "Giriş şu anda yapılamıyor. Lütfen sistem yöneticisine başvurun.");
            }
        }

        /// <returns>null: AD'de doğrulanamadı → yerel hesap denensin.</returns>
        private async Task<(bool ok, string message)?> TryActiveDirectoryLoginAsync(string input, string password, bool rememberMe)
        {
            var (sam, upn) = SplitUser(input, _ad.Domain);

            using var ctx = new PrincipalContext(ContextType.Domain, _ad.Domain);

            bool adValid = ctx.ValidateCredentials(upn, password) || ctx.ValidateCredentials(sam, password);
            if (!adValid)
                return null;

            var principal =
                UserPrincipal.FindByIdentity(ctx, IdentityType.UserPrincipalName, upn) ??
                UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, sam);

            // Doğrudan grup üyelikleri (claims factory'deki memberOf ile aynı kapsam). IAdUserService.GetUserGroupsAsync
            // KULLANILMAZ: o liste Domain Admins gibi ayrıcalıklı grupları gizler ve RoleMapping'de eşleşmezdi.
            var groups = principal == null
                ? new List<string>()
                : principal.GetGroups()
                    .SelectMany(g => new[] { g.SamAccountName, g.Name }) // RoleMapping CN veya sAMAccountName ile yazılmış olabilir
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .ToList();
            var roles = MapRoles(groups, _roleMap);

            if (roles.Count == 0)
            {
                await AuditLoginAsync(sam, false, "ad", "no_mapped_role");
                return (false, "Bu panele erişim yetkiniz bulunmuyor. Yetki için sistem yöneticisine başvurun.");
            }

            var user = await _userManager.FindByNameAsync(sam);
            if (user == null)
            {
                user = new IdentityUser { UserName = sam, Email = upn, EmailConfirmed = true };
                var create = await _userManager.CreateAsync(user);
                if (!create.Succeeded)
                {
                    _logger.LogError("AD kullanıcısı için yerel kayıt oluşturulamadı: {Errors}",
                        string.Join("; ", create.Errors.Select(e => e.Code)));
                    await AuditLoginAsync(sam, false, "ad", "identity_create_failed");
                    return (false, "Giriş şu anda yapılamıyor. Lütfen sistem yöneticisine başvurun.");
                }
            }

            // Roller her girişte AD'ye göre senkronize edilir (AD'den çıkarılan yetki panelden de düşer).
            var current = await _userManager.GetRolesAsync(user);
            var toRemove = current.Except(roles, StringComparer.OrdinalIgnoreCase).ToList();
            var toAdd = roles.Except(current, StringComparer.OrdinalIgnoreCase).ToList();

            if (toRemove.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, toRemove);

            foreach (var role in toAdd)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                    await _roleManager.CreateAsync(new IdentityRole(role));
                await _userManager.AddToRoleAsync(user, role);
            }

            await _signInManager.SignOutAsync();
            await _signInManager.SignInAsync(user, rememberMe);
            await AuditLoginAsync(sam, true, "ad", metadata: new { Roles = roles });

            return (true, "Giriş başarılı.");
        }

        internal static List<string> MapRoles(IEnumerable<string> adGroupNamesOrDns, RoleMappingOptions map)
        {
            var groups = adGroupNamesOrDns.Select(NormalizeGroup).Where(g => g.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return map
                .Where(kv => kv.Value != null && kv.Value.Any(g => groups.Contains(g.Trim())))
                .Select(kv => kv.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Task AuditLoginAsync(string userName, bool success, string method, string? errorCode = null, object? metadata = null)
            => _audit.RecordAsync(new AuditRecord(success ? AuditActions.LoginSucceeded : AuditActions.LoginFailed)
            {
                ActorUserName = userName,
                ActorType = success ? "User" : "Anonymous",
                ResourceType = "Session",
                Success = success,
                ErrorCode = errorCode,
                Metadata = new { Method = method, Detail = metadata },
            });

        public async Task LogoutAsync()
        {
            await _audit.RecordAsync(new AuditRecord(AuditActions.Logout) { ResourceType = "Session" });
            await _signInManager.SignOutAsync();
        }

        internal static (string sam, string upn) SplitUser(string input, string domain)
        {
            input = input.Trim();

            if (input.Contains('@'))
                return (input.Split('@')[0], input);

            if (input.Contains('\\'))
            {
                var p = input.Split('\\', 2);
                return (p[1], $"{p[1]}@{domain}");
            }

            return (input, $"{input}@{domain}");
        }

        private static string NormalizeGroup(string nameOrDn)
        {
            if (string.IsNullOrWhiteSpace(nameOrDn)) return "";
            var cn = nameOrDn.Split(',').FirstOrDefault(p => p.TrimStart().StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
            return (cn != null ? cn.Trim().Substring(3) : nameOrDn).Trim();
        }
    }
}
