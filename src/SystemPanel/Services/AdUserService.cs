using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Configuration;
using SystemPanel.Models;
using SystemPanel.ViewModels.AdUser;

namespace SystemPanel.Services
{
    public class AdUserService : IAdUserService, IDisposable
    {
        private readonly ILogger<AdUserService> _logger;
        private readonly LdapOptions _options;
        private readonly IMemoryCache _cache;

        // AD yavasladiginda siteyi öldürmesin diye eszamanli LDAP aramalarini sinirla
        private static readonly SemaphoreSlim _ldapGate = new(12, 12);

        // Default güvenli timeoutlar
        private static readonly TimeSpan DefaultLdapTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DefaultCommitTimeout = TimeSpan.FromSeconds(5);

        private static readonly string[] CriticalGroups =
        {
            "Domain Admins",
            "Enterprise Admins",
            "Schema Admins",
            "Administrators",
            "Account Operators",
            "Server Operators",
            "Backup Operators",
            "Print Operators",
            "DnsAdmins",
            "RAS and IAS Servers"
        };

        private readonly ActiveDirectoryOptions _ad;

        public AdUserService(
            ILogger<AdUserService> logger,
            IOptions<LdapOptions> options,
            IOptions<ActiveDirectoryOptions> ad,
            IMemoryCache cache)
        {
            _logger = logger;
            _options = options.Value;
            _ad = ad.Value;
            _cache = cache;
        }

        /// <summary>Yerleşik ayrıcalıklı gruplar + yapılandırmadaki ek korumalı gruplar.</summary>
        public bool IsProtectedGroup(string groupDnOrName)
        {
            var name = groupDnOrName.Contains('=') ? ExtractCn(groupDnOrName) : groupDnOrName;
            return IsCriticalGroup(name) || _ad.ProtectedGroups.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        // ===================================================
        // ? EDIT EKRANI IÇIN HAFIF OKUMA (DONMAZ)
        // ===================================================
        public async Task<AdUser?> GetUserForEditAsync(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                string dn = id;
                if (!(id.Contains("=") && id.Contains(",")))
                    dn = await GetDistinguishedName(id);

                if (string.IsNullOrWhiteSpace(dn))
                    return null;

                var res = await SearchAsync(
                    $"(distinguishedName={LdapEscape(dn)})",
                    _options.BaseDn,
                    new[]
                    {
                        "distinguishedName","sAMAccountName","userPrincipalName",
                        "displayName","givenName","sn","mail","mailNickname",
                        "mobile","telephoneNumber","department","description",
                        "userAccountControl","lockoutTime"
                    },
                    sizeLimit: 1,
                    ct: ct);

                var sr = res.FirstOrDefault();
                if (sr == null) return null;

                int uac = GetIntProp(sr, "userAccountControl");

                return new AdUser
                {
                    Id = GetProp(sr, "distinguishedName"),
                    DistinguishedName = GetProp(sr, "distinguishedName"),
                    SamAccountName = GetProp(sr, "sAMAccountName"),
                    UserPrincipalName = GetProp(sr, "userPrincipalName"),

                    UserName = GetProp(sr, "sAMAccountName"),
                    DisplayName = GetProp(sr, "displayName"),
                    GivenName = GetProp(sr, "givenName"),
                    Surname = GetProp(sr, "sn"),

                    Mail = GetProp(sr, "mail"),
                    MailNickname = GetProp(sr, "mailNickname"),
                    Email = GetProp(sr, "mail"),

                    Mobile = GetProp(sr, "mobile"),
                    TelephoneNumber = GetProp(sr, "telephoneNumber"),

                    Department = GetProp(sr, "department"),
                    Description = GetProp(sr, "description"),

                    Enabled = (uac & 0x2) == 0,
                    IsLocked = IsLockedOut(sr),

                    OuPath = ExtractOuPath(GetProp(sr, "distinguishedName")),
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AD edit load failed for {Id}. Returning fail-soft model.", id);
                return new AdUser { Id = id, DistinguishedName = id };
            }
        }

        // ===================================================
        // OU ARAMA – (organizationalUnit)
        // ===================================================
        public async Task<List<OrganizationalUnit>> GetOrganizationalUnitsByNameAsync(
            string name,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            string escaped = LdapEscape(name ?? string.Empty);

            string filter = string.IsNullOrWhiteSpace(name)
                ? "(objectClass=organizationalUnit)"
                : $"(&(objectClass=organizationalUnit)(|(ou=*{escaped}*)(name=*{escaped}*)))";

            var props = new[] { "distinguishedName", "ou", "name", "description" };
            var results = await SearchAsync(filter, _options.BaseDn, props, sizeLimit: 0, ct: ct);

            var list = new List<OrganizationalUnit>();
            foreach (SearchResult r in results)
            {
                string dn = GetProp(r, "distinguishedName");
                string ou = GetProp(r, "ou");
                string nameProp = GetProp(r, "name");
                string desc = GetProp(r, "description");

                var displayName = string.IsNullOrWhiteSpace(ou) ? nameProp : ou;

                list.Add(new OrganizationalUnit
                {
                    DistinguishedName = dn,
                    Name = displayName,
                    Description = desc
                });
            }

            return list
                .Where(x => !string.IsNullOrWhiteSpace(x.DistinguishedName))
                .OrderBy(x => x.Name)
                .ToList();
        }

        // ===================================================
        // Kullanicinin DN bilgisini çözen metod
        // ===================================================
        public async Task<string> GetDistinguishedName(string samOrUpnOrDn)
        {
            if (string.IsNullOrWhiteSpace(samOrUpnOrDn))
                return string.Empty;

            if (samOrUpnOrDn.Contains("=") && samOrUpnOrDn.Contains(","))
                return samOrUpnOrDn; // zaten DN

            string escaped = LdapEscape(samOrUpnOrDn);
            string filter = $"(|(sAMAccountName={escaped})(userPrincipalName={escaped}))";

            var results = await SearchAsync(filter, _options.BaseDn, new[] { "distinguishedName" }, sizeLimit: 1);
            return GetProp(results.FirstOrDefault(), "distinguishedName");
        }

        // ===================================================
        // Bilgisayarlari OU'ya göre getiren metod
        // ===================================================
        public async Task<List<AdComputer>> GetComputersByProvinceAsync(string provinceOrDn)
        {
            string baseDn = string.IsNullOrWhiteSpace(provinceOrDn)
                ? _options.BaseDn
                : BuildOuDn(provinceOrDn);

            string filter = "(&(objectCategory=computer)(objectClass=computer))";

            var props = new[]
            {
                "distinguishedName",
                "name",
                "dNSHostName",
                "operatingSystem",
                "operatingSystemVersion"
            };

            var results = await SearchAsync(filter, baseDn, props, sizeLimit: 0);

            return results.Select(r => new AdComputer
            {
                DistinguishedName = GetProp(r, "distinguishedName"),
                Name = GetProp(r, "name"),
                DnsHostName = GetProp(r, "dNSHostName"),
                OperatingSystem = GetProp(r, "operatingSystem"),
                OperatingSystemVersion = GetProp(r, "operatingSystemVersion")
            }).ToList();
        }

        // ===================================================
        // Kullanici Arama
        // ===================================================
        public async Task<IEnumerable<AdUser>> GetAdUsers(string searchTerm)
            => await GetAdUsersInOuAsyncCore(searchTerm, _options.BaseDn);

        public async Task<List<AdUser>> GetUsersByProvinceAsync(string provinceOrDn)
            => await GetAdUsersInOuAsyncCore(string.Empty, provinceOrDn);

        public Task<List<AdUser>> GetAdUsersInOuAsync(
            string searchTerm,
            string provinceOrDn,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return GetAdUsersInOuAsyncCore(searchTerm, provinceOrDn, ct);
        }

        private async Task<List<AdUser>> GetAdUsersInOuAsyncCore(string searchTerm, string provinceOrDn, CancellationToken ct = default)
        {
            string baseDn = string.IsNullOrWhiteSpace(provinceOrDn)
                ? _options.BaseDn
                : BuildOuDn(provinceOrDn);

            string term = (searchTerm ?? string.Empty).Trim();
            string escaped = LdapEscape(term);

            string filter = string.IsNullOrWhiteSpace(term)
                ? "(&(objectCategory=person)(objectClass=user))"
                : $"(&(objectCategory=person)(objectClass=user)" +
                  $"(|(sAMAccountName=*{escaped}*)(displayName=*{escaped}*)(mail=*{escaped}*)(userPrincipalName=*{escaped}*)))";

            var props = new[]
            {
                "distinguishedName",
                "sAMAccountName",
                "userPrincipalName",
                "displayName",
                "mail",
                "userAccountControl",
                "lockoutTime"
            };

            var results = await SearchAsync(filter, baseDn, props, sizeLimit: 0, ct: ct);

            var users = new List<AdUser>();
            foreach (SearchResult r in results)
            {
                string dn = GetProp(r, "distinguishedName");
                string sam = GetProp(r, "sAMAccountName");
                string upn = GetProp(r, "userPrincipalName");
                string disp = GetProp(r, "displayName");
                string mail = GetProp(r, "mail");

                int uac = GetIntProp(r, "userAccountControl");
                bool enabled = (uac & 0x2) == 0;

                bool locked = IsLockedOut(r);

                users.Add(new AdUser
                {
                    DistinguishedName = dn,
                    SamAccountName = sam,
                    UserPrincipalName = upn,
                    UserName = sam,
                    DisplayName = disp,
                    Mail = mail,
                    Email = mail,
                    Enabled = enabled,
                    IsLocked = locked,
                    OuPath = ExtractOuPath(dn)
                });
            }

            return users;
        }

public async Task<List<AdGroup>> GetUserGroupObjectsAsync(string samOrDn)
{
    var list = new List<AdGroup>();

    var userDn = await GetDistinguishedName(samOrDn);
    if (string.IsNullOrWhiteSpace(userDn)) return list;

    var res = await SearchAsync(
        $"(distinguishedName={LdapEscape(userDn)})",
        _options.BaseDn,
        new[] { "memberOf" },
        sizeLimit: 1);

    var sr = res.FirstOrDefault();
    if (sr == null) return list;

    if (!sr.Properties.Contains("memberOf")) return list;

    foreach (var v in sr.Properties["memberOf"])
    {
        var groupDn = v?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(groupDn)) continue;

        // group objesini çek: name/sam/description
        var gRes = await SearchAsync(
            $"(distinguishedName={LdapEscape(groupDn)})",
            _options.BaseDn,
            new[] { "distinguishedName", "name", "sAMAccountName", "description" },
            sizeLimit: 1);

        var g = gRes.FirstOrDefault();
        if (g == null) continue;

        list.Add(new AdGroup
        {
            DistinguishedName = GetProp(g, "distinguishedName"),
            Name = GetProp(g, "name"),
            SamAccountName = GetProp(g, "sAMAccountName"),
            Description = GetProp(g, "description")
        });
    }

    return list;
}


/// <summary>
/// İl yönetici gruplarının açıklamasından il adlarını çıkarır. Grup soneki ve hariç tutulan gruplar
/// yapılandırmadan gelir (ActiveDirectory:Scope); sonek boşsa hiçbir il döndürülmez.
/// </summary>
public static List<string> ExtractProvincesFromLocalAdminGroups(IEnumerable<AdGroup> groups, ScopeDerivationOptions scope)
{
    var provinces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var g in groups)
    {
        var sam = (g.SamAccountName ?? g.Name ?? "").Trim();

        if (string.IsNullOrWhiteSpace(scope.LocalAdminGroupSuffix))
            break;

        if (scope.ExcludedLocalAdminGroups.Contains(sam, StringComparer.OrdinalIgnoreCase))
            continue;

        if (!sam.EndsWith(scope.LocalAdminGroupSuffix, StringComparison.OrdinalIgnoreCase))
            continue;

        // Description: "Adana Local Admin"
        var desc = (g.Description ?? "").Trim();
        if (string.IsNullOrWhiteSpace(desc)) continue;

        var firstWord = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!string.IsNullOrWhiteSpace(firstWord))
            provinces.Add(firstWord);
    }

    return provinces.ToList();
}




        // ===================================================
        // LocalAdmin Scope
        // ===================================================
        public async Task<LocalAdminScopeViewModel> GetLocalAdminScopeAsync(
            string ou,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Bu ikisi pahali olabilir ama LDAP timeout var.
            var users = await GetUsersByProvinceAsync(ou);
            var computers = await GetComputersByProvinceAsync(ou);

            return new LocalAdminScopeViewModel
            {
                RequestedOu = ou,
                IsAuthorized = true,
                AllowedOuDns = new List<string> { ou },
                Message = $"Kullanici: {users?.Count ?? 0}, Bilgisayar: {computers?.Count ?? 0}"
            };
        }

        // ===================================================
        // LAPS
        // ===================================================
        public async Task<string> GetComputerDistinguishedName(string computerName)
        {
            if (string.IsNullOrWhiteSpace(computerName))
                return string.Empty;

            string escaped = LdapEscape(computerName);

            var result = await SearchAsync(
                $"(sAMAccountName={escaped}$)",
                _options.BaseDn,
                new[] { "distinguishedName" },
                sizeLimit: 1);

            return GetProp(result.FirstOrDefault(), "distinguishedName");
        }

        // ===================================================
        // OU TREE
        // ===================================================
        public async Task<OuTreeDto> GetOuTreeAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            string rootDn = _options.BaseDn;
            var props = new[] { "name", "distinguishedName" };

            var results = await SearchAsync("(objectClass=organizationalUnit)", rootDn, props, sizeLimit: 0, ct: ct);

            var allOus = results
                .Select(r => new OuTreeDto
                {
                    OuName = GetProp(r, "name"),
                    DistinguishedName = GetProp(r, "distinguishedName")
                })
                .ToList();

            var lookup = allOus.ToDictionary(x => x.DistinguishedName, x => x, StringComparer.OrdinalIgnoreCase);

            foreach (var ou in allOus)
            {
                var parentDn = string.Join(",", ou.DistinguishedName.Split(',').Skip(1));
                if (lookup.TryGetValue(parentDn, out var parent))
                    parent.Children.Add(ou);
            }

            return allOus.FirstOrDefault(x => IsSameDn(x.DistinguishedName, rootDn))
                ?? new OuTreeDto { OuName = "ROOT", DistinguishedName = rootDn };
        }

        // ===================================================
        // GROUP / AUTH
        // ===================================================
        public async Task<bool> IsUserInGroupByDnAsync(string userDn, string groupDn)
        {
            if (string.IsNullOrWhiteSpace(userDn) || string.IsNullOrWhiteSpace(groupDn))
                return false;

            string filter =
                $"(&(objectClass=group)(distinguishedName={LdapEscape(groupDn)})(member={LdapEscape(userDn)}))";

            var res = await SearchAsync(filter, _options.BaseDn, new[] { "distinguishedName" }, sizeLimit: 1);
            return res.Any();
        }

        public Task<bool> IsUserInGroupByNameAsync(string userDn, string groupName)
            => Task.FromResult(false);

        public async Task<HashSet<string>> GetAuthorizedOuScopesForUserAsync(string samOrUserDn)
        {
            var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var userDn = await GetDistinguishedName(samOrUserDn);
            if (string.IsNullOrWhiteSpace(userDn))
                return scopes;

            var baseDn = _options.BaseDn;

            var domainAdminsDn = $"CN=Domain Admins,CN=Users,{baseDn}";
            var enterpriseAdminsDn = $"CN=Enterprise Admins,CN=Users,{baseDn}";

            bool isDomainAdmin = await IsUserInGroupByDnAsync(userDn, domainAdminsDn);
            bool isEnterpriseAdmin = await IsUserInGroupByDnAsync(userDn, enterpriseAdminsDn);

            if (isDomainAdmin || isEnterpriseAdmin)
            {
                scopes.Add(baseDn);
                return scopes;
            }

            // OU çıkarılamazsa kapsam BOŞ döner (önceden tüm domain kökü dönüyordu: fail-open).
            string userOu = ExtractOuFull(userDn, baseDn);
            if (!string.IsNullOrWhiteSpace(userOu) && !IsSameDn(userOu, baseDn))
                scopes.Add(userOu);

            return scopes;
        }

        public async Task<bool> IsUserAuthorizedForScopeAsync(string samOrUserDn, string requestedScopeDn)
        {
            var scopes = await GetAuthorizedOuScopesForUserAsync(samOrUserDn);
            if (scopes.Count == 0) return false;

            foreach (var scope in scopes)
            {
                if (IsSameDn(scope, requestedScopeDn) || IsUnderDn(requestedScopeDn, scope))
                    return true;
            }

            return false;
        }

        private static string ExtractCn(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn)) return "";
            var parts = dn.Split(',').Select(x => x.Trim());
            var cn = parts.FirstOrDefault(x => x.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
            return cn?.Substring(3) ?? "";
        }

        private static string ExtractSam(string userDn) => ExtractCn(userDn);

        private bool IsCriticalGroup(string name)
        {
            return CriticalGroups.Any(c =>
                string.Equals(c, name, StringComparison.OrdinalIgnoreCase)
            );
        }

        private bool IsLocalAdminGroup(string name)
        {
            return name.Contains("LocalAdmin", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Local-Admin", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LA-", StringComparison.OrdinalIgnoreCase);
        }

        private string GetUserOu(string userDn, string baseDn)
        {
            if (string.IsNullOrWhiteSpace(userDn)) return baseDn;

            var parts = userDn.Split(',').Select(p => p.Trim()).ToList();
            int start = parts[0].StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            var ouParts = parts
                .Skip(start)
                .TakeWhile(x => x.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (ouParts.Count == 0)
                return baseDn;

            var tail = parts.Skip(start + ouParts.Count); // DC=...

            return string.Join(",", ouParts.Concat(tail));
        }

        public async Task<List<AdGroup>> GetAllowedGroupsForUser(string sam)
        {
            var result = new List<AdGroup>();

            string userDn = await GetDistinguishedName(sam);
            if (string.IsNullOrWhiteSpace(userDn))
                return result;

            string baseDn = _options.BaseDn;

            bool isAdmin = false;
            bool isOperator = false;
            bool isLocalAdmin = false;

            var domainAdminsDn = $"CN=Domain Admins,CN=Users,{baseDn}";
            var enterpriseAdminsDn = $"CN=Enterprise Admins,CN=Users,{baseDn}";

            if (await IsUserInGroupByDnAsync(userDn, domainAdminsDn)
                || await IsUserInGroupByDnAsync(userDn, enterpriseAdminsDn))
            {
                isAdmin = true;
            }
            else
            {
                if (userDn.Contains("OU=LocalAdmins", StringComparison.OrdinalIgnoreCase))
                    isLocalAdmin = true;
                else
                    isOperator = true;
            }

            // ? Pahali sorgu: cache’li kullan
            var all = await GetAllSecurityGroupsCachedAsync();

            if (isAdmin)
                return all;

            if (isOperator)
            {
                string userOu = GetUserOu(userDn, baseDn);

                return all
                    .Where(g => IsUnderDn(g.DistinguishedName, userOu))
                    .Where(g => !IsCriticalGroup(g.Name))
                    .ToList();
            }

            if (isLocalAdmin)
            {
                return all
                    .Where(g => IsLocalAdminGroup(g.Name))
                    .Where(g => !IsCriticalGroup(g.Name))
                    .ToList();
            }

            return new List<AdGroup>();
        }

        public async Task<List<string>> GetUserGroupsAsync(string userDn)
        {
            var list = new List<string>();

            if (string.IsNullOrWhiteSpace(userDn))
                return list;

            var results = await SearchAsync(
                $"(member={LdapEscape(userDn)})",
                _options.BaseDn,
                new[] { "name" },
                sizeLimit: 0
            );

            foreach (SearchResult sr in results)
            {
                string name = GetProp(sr, "name");
                if (!string.IsNullOrWhiteSpace(name) && !IsCriticalGroup(name))
                    list.Add(name);
            }

            return list.OrderBy(x => x).ToList();
        }

        // ? Cache’li (UI donmaz)
        public async Task<List<AdGroup>> GetAllSecurityGroupsAsync(string search = "")
        {
            // arama varsa cache kullanma (kullanici filtrasyonu için)
            if (!string.IsNullOrWhiteSpace(search))
                return await GetAllSecurityGroupsNoCacheAsync(search);

            return await GetAllSecurityGroupsCachedAsync();
        }

        private async Task<List<AdGroup>> GetAllSecurityGroupsCachedAsync()
        {
            return await _cache.GetOrCreateAsync("AD_ALL_GROUPS", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);

                var groups = await GetAllSecurityGroupsNoCacheAsync("");
                return groups;
            }) ?? new List<AdGroup>();
        }

        private async Task<List<AdGroup>> GetAllSecurityGroupsNoCacheAsync(string search)
        {
            string escaped = LdapEscape(search ?? "");

            string filter = string.IsNullOrWhiteSpace(search)
                ? "(&(objectClass=group))"
                : $"(&(objectClass=group)(|(cn=*{escaped}*)(name=*{escaped}*)(sAMAccountName=*{escaped}*)))";

            var props = new[]
            {
                "distinguishedName", "name", "sAMAccountName", "description"
            };

            var results = await SearchAsync(filter, _options.BaseDn, props, sizeLimit: 0);

            return results.Select(r => new AdGroup
            {
                DistinguishedName = GetProp(r, "distinguishedName"),
                Name = GetProp(r, "name"),
                SamAccountName = GetProp(r, "sAMAccountName"),
                Description = GetProp(r, "description")
            })
                .OrderBy(x => x.Name)
                .ToList();
        }

        // low-level wrapper
        public async Task<bool> AddUserToGroupByDn(string userDn, string groupDn)
        {
            try
            {
                using var group = GetEntry(groupDn);
                group.Properties["member"].Add(userDn);

                await RunWithTimeout(() => group.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddUserToGroupByDn failed");
                return false;
            }
        }

        public async Task<bool> RemoveUserFromGroupByDn(string userDn, string groupDn)
        {
            try
            {
                using var group = GetEntry(groupDn);

                if (group.Properties["member"].Contains(userDn))
                {
                    group.Properties["member"].Remove(userDn);
                    await RunWithTimeout(() => group.CommitChanges(), DefaultCommitTimeout);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveUserFromGroupByDn failed");
                return false;
            }
        }

        // ===================================================
        // LDAP ENTRY + SEARCH CORE (DONMAYAN)
        // ===================================================
        public DirectoryEntry GetEntry(string dnOrLdapPath)
        {
            // BindUser boşsa servis kimliği (gMSA) kullanılır; parola __ENV__ ile ortamdan okunabilir.
            return _options.CreateDirectoryEntry(dnOrLdapPath);
        }

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string ldapFilter,
            string? searchBaseDn = null,
            string[]? propertiesToLoad = null,
            int? sizeLimit = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Gate: asiri paralel LDAP çagrisi donmayi/CPU’yu artirir
            await _ldapGate.WaitAsync(ct);
            try
            {
                var list = new List<SearchResult>();

                string baseDn = searchBaseDn ?? _options.BaseDn;

                DirectoryEntry entry;
                try
                {
                    entry = GetEntry(baseDn);
                    _ = entry.NativeObject; // bind test
                }
                catch (Exception ex)
                {
                    // Önceden burada domain köküne düşülüyordu: kapsamı bir OU ile sınırlanmış arama
                    // (ör. LocalAdmin) bind hatasında TÜM domain sonuçlarını döndürebiliyordu.
                    _logger.LogWarning(ex, "LDAP bind başarısız (baseDn={Dn}); boş sonuç döndürülüyor.", baseDn);
                    return Array.Empty<SearchResult>();
                }

                using (entry)
                using (var ds = new DirectorySearcher(entry))
                {
                    ds.Filter = ldapFilter;
                    ds.PageSize = 500;
                    ds.SizeLimit = sizeLimit ?? 0;

                    // ? Donmayi kesen asil ayarlar
                    ds.ClientTimeout = DefaultLdapTimeout;
                    ds.ServerTimeLimit = DefaultLdapTimeout;

                    // Referral chasing bazi ortamlarda (Tier/GC/Forest) request’i uzatir
                    ds.ReferralChasing = ReferralChasingOption.None;

                    if (propertiesToLoad != null)
                    {
                        ds.PropertiesToLoad.Clear();
                        foreach (var p in propertiesToLoad)
                            ds.PropertiesToLoad.Add(p);
                    }

                    using var results = ds.FindAll(); // sync ama timeout’lu
                    foreach (SearchResult r in results)
                        list.Add(r);
                }

                return list;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LDAP Search failed. filter={Filter}", ldapFilter);
                return Array.Empty<SearchResult>();
            }
            finally
            {
                _ldapGate.Release();
            }
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                using var entry = GetEntry(_options.BaseDn);
                _ = entry.NativeObject;
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LDAP Ping failed");
                return await Task.FromResult(false);
            }
        }

        // ===================================================
        // HELPERS
        // ===================================================
        private static string GetProp(SearchResult? r, string name)
        {
            if (r == null) return string.Empty;
            return r.Properties.Contains(name)
                ? r.Properties[name][0]?.ToString() ?? string.Empty
                : string.Empty;
        }

        private static int GetIntProp(SearchResult? r, string name)
        {
            if (r?.Properties.Contains(name) != true) return 0;
            int.TryParse(r.Properties[name][0]?.ToString(), out int v);
            return v;
        }

        private static DateTime? ParseDate(SearchResult? r, string attrName)
        {
            if (r?.Properties.Contains(attrName) != true || r.Properties[attrName].Count == 0)
                return null;

            var val = r.Properties[attrName][0];

            if (val is DateTime dt)
                return dt;

            if (DateTime.TryParse(val.ToString(), out var parsed))
                return parsed;

            return null;
        }

        private static string ExtractOuPath(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn)) return string.Empty;
            var parts = dn.Split(',')
                .Where(x => x.Trim().StartsWith("OU=", StringComparison.OrdinalIgnoreCase));
            return string.Join(",", parts);
        }

        private static string ExtractOuFull(string userDn, string baseDn)
        {
            if (string.IsNullOrWhiteSpace(userDn))
                return baseDn;

            var parts = userDn.Split(',').Select(x => x.Trim()).ToList();
            int start = parts[0].StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            var ouParts = parts.Skip(start)
                .TakeWhile(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (ouParts.Count == 0)
                return baseDn;

            var tail = parts.Skip(start + ouParts.Count);
            return string.Join(",", ouParts.Concat(tail));
        }

        public string ResolveBaseDn() => _options.BaseDn;

        public IEnumerable<string> EnumerateDomainBaseDns()
        {
            if (!string.IsNullOrWhiteSpace(_options.BaseDn))
                yield return _options.BaseDn;
        }

        public string BuildOuDn(string provinceOrDn)
        {
            if (string.IsNullOrWhiteSpace(provinceOrDn))
                return _options.BaseDn;

            string v = provinceOrDn.Trim();

            if (v.Contains("DC=", StringComparison.OrdinalIgnoreCase))
                return v;

            if (v.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                return $"{v},{_options.BaseDn}";

            return $"OU={v},{_options.BaseDn}";
        }

        public bool IsSameDn(string a, string b)
            => NormalizeDn(a) == NormalizeDn(b);

        private static string NormalizeDn(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn)) return "";
            return string.Join(",", dn.Split(',').Select(p => p.Trim().ToUpperInvariant()));
        }

        public bool IsUnderDn(string child, string root)
        {
            var c = NormalizeDn(child);
            var r = NormalizeDn(root);

            if (string.IsNullOrEmpty(c) || string.IsNullOrEmpty(r))
                return false;

            if (c == r)
                return true;

            return c.EndsWith("," + r, StringComparison.OrdinalIgnoreCase);
        }

        private static string LdapEscape(string val)
        {
            if (val == null) return string.Empty;
            return val.Replace("\\", "\\5c")
                .Replace("*", "\\2a")
                .Replace("(", "\\28")
                .Replace(")", "\\29")
                .Replace("\0", "\\00");
        }

        private static bool IsLockedOut(SearchResult sr)
        {
            if (!sr.Properties.Contains("lockoutTime") || sr.Properties["lockoutTime"].Count == 0)
                return false;

            long.TryParse(sr.Properties["lockoutTime"][0].ToString(), out long lockoutValue);
            return lockoutValue != 0;
        }

        // ===================================================
        // USERS CRUD
        // ===================================================
        public Task<IEnumerable<AdUser>> GetAllAdUsersAsync()
            => Task.FromResult<IEnumerable<AdUser>>(new List<AdUser>());

        public async Task<AdUser?> GetUserByIdAsync(string id)
        {
            // Full detay (kullaniyorsan kalsin). Edit ekraninda GetUserForEditAsync kullan.
            if (string.IsNullOrWhiteSpace(id))
                return null;

            string dn = id;
            if (!(id.Contains("=") && id.Contains(",")))
            {
                dn = await GetDistinguishedName(id);
                if (string.IsNullOrWhiteSpace(dn))
                    return null;
            }

            var res = await SearchAsync(
                $"(distinguishedName={LdapEscape(dn)})",
                _options.BaseDn,
                new[]
                {
                    "distinguishedName","sAMAccountName","userPrincipalName",
                    "displayName","givenName","sn","mail","mailNickname",
                    "mobile","telephoneNumber","department","description",
                    "userAccountControl","whenCreated","whenChanged","lockoutTime","adminCount"
                },
                sizeLimit: 1
            );

            var sr = res.FirstOrDefault();
            if (sr == null) return null;

            int uac = GetIntProp(sr, "userAccountControl");
            bool enabled = (uac & 0x2) == 0;
            bool locked = IsLockedOut(sr);

            var dnNorm = GetProp(sr, "distinguishedName");

            return new AdUser
            {
                Id = dnNorm,
                DistinguishedName = dnNorm,
                SamAccountName = GetProp(sr, "sAMAccountName"),
                UserPrincipalName = GetProp(sr, "userPrincipalName"),

                UserName = GetProp(sr, "sAMAccountName"),
                DisplayName = GetProp(sr, "displayName"),
                GivenName = GetProp(sr, "givenName"),
                Surname = GetProp(sr, "sn"),

                Mail = GetProp(sr, "mail"),
                MailNickname = GetProp(sr, "mailNickname"),
                Email = GetProp(sr, "mail"),

                Mobile = GetProp(sr, "mobile"),
                TelephoneNumber = GetProp(sr, "telephoneNumber"),

                Department = GetProp(sr, "department"),
                Description = GetProp(sr, "description"),

                Enabled = enabled,
                IsLocked = locked,
                OuPath = ExtractOuPath(dnNorm),

                IsPrivileged = GetIntProp(sr, "adminCount") == 1,
                WhenCreated = ParseDate(sr, "whenCreated"),
                WhenChanged = ParseDate(sr, "whenChanged")
            };
        }

        public async Task<bool> CreateUserAsync(AdUser user, string password)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.DistinguishedName))
                return false;

            try
            {
                using var ouEntry = GetEntry(user.DistinguishedName);

                string cn = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.SamAccountName ?? user.UserName
                    : user.DisplayName;

                var newUser = ouEntry.Children.Add($"CN={cn}", "user");

                void Set(string attr, string value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        newUser.Properties[attr].Value = value;
                }

                Set("sAMAccountName", user.SamAccountName ?? user.UserName);
                Set("userPrincipalName", user.UserPrincipalName ?? user.Mail);
                Set("displayName", user.DisplayName);
                Set("givenName", user.GivenName);
                Set("sn", user.Surname);
                Set("mail", user.Mail);

                await RunWithTimeout(() => newUser.CommitChanges(), DefaultCommitTimeout);

                if (!string.IsNullOrWhiteSpace(password))
                {
                    newUser.Invoke("SetPassword", new object[] { password });
                    newUser.Properties["pwdLastSet"].Value = -1;
                }

                int uac = 0x200; // NORMAL_ACCOUNT
                newUser.Properties["userAccountControl"].Value = uac & ~0x2;
                await RunWithTimeout(() => newUser.CommitChanges(), DefaultCommitTimeout);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateUserAsync failed");
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(string id, EditAdUserViewModel m)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            try
            {
                using var entry = GetEntry(id);

                void Set(string attr, string value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (entry.Properties[attr].Count > 0)
                            entry.Properties[attr].Clear();
                    }
                    else
                    {
                        entry.Properties[attr].Value = value;
                    }
                }

                Set("givenName", m.GivenName);
                Set("sn", m.Surname);
                Set("displayName", m.DisplayName);
                Set("mail", m.Mail);
                Set("mailNickname", m.MailNickname);
                Set("userPrincipalName", m.UserPrincipalName);
                Set("mobile", m.Mobile);
                Set("telephoneNumber", m.TelephoneNumber);
                Set("department", m.Department);
                Set("description", m.Description);

                await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AD User update failed for {Dn}", id);
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            try
            {
                using var entry = GetEntry(id);
                using var parent = entry.Parent;
                parent.Children.Remove(entry);

                await RunWithTimeout(() => parent.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteUserAsync failed for {Id}", id);
                return false;
            }
        }

        // Password & Lock (bazilari sende stub; oldugu gibi birakiyorum)
        // Önceden boş stub'tı: UserManagement/ResetAdPassword hiç değiştirilmemiş bir parolayı SMS ile gönderiyordu.
        public async Task ResetPasswordWithServiceAccount(string targetDn, string newPassword)
        {
            if (!await ResetPasswordManualAsync(targetDn, newPassword))
                throw new InvalidOperationException("Parola sıfırlanamadı.");
        }

        /// <summary>Kullanıcı bir sonraki oturum açışında parolasını değiştirmek zorunda kalır (pwdLastSet = 0).</summary>
        public async Task ForceChangeAtNextLogon(string targetDn)
        {
            using var entry = GetEntry(targetDn);
            entry.Properties["pwdLastSet"].Value = 0;
            await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
        }

        public async Task<bool> IsUserLockedAsync(string userDn)
        {
            var res = await SearchAsync($"(distinguishedName={LdapEscape(userDn)})", _options.BaseDn, new[] { "lockoutTime" }, sizeLimit: 1);
            var sr = res.FirstOrDefault();
            return sr != null && IsLockedOut(sr);
        }

        public async Task<bool> ResetPasswordManualAsync(string userDn, string newPassword)
        {
            try
            {
                using var entry = GetEntry(userDn);
                entry.Invoke("SetPassword", new object[] { newPassword });
                entry.Properties["pwdLastSet"].Value = -1;

                await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResetPasswordManualAsync failed for {userDn}", userDn);
                return false;
            }
        }

        public async Task<bool> ResetPasswordAndUnlockAsync(string userDn, string newPassword)
        {
            try
            {
                using var entry = GetEntry(userDn);
                entry.Invoke("SetPassword", new object[] { newPassword });
                entry.Properties["pwdLastSet"].Value = -1;
                entry.Properties["lockoutTime"].Value = 0;

                await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResetPasswordAndUnlockAsync failed for {userDn}", userDn);
                return false;
            }
        }

        public async Task<bool> DisableUserAsync(string userDn)
        {
            if (string.IsNullOrWhiteSpace(userDn))
                return false;

            try
            {
                using var entry = GetEntry(userDn);
                entry.RefreshCache(new[] { "userAccountControl" });

                int uac = 0;
                if (entry.Properties["userAccountControl"].Count > 0)
                    uac = (int)entry.Properties["userAccountControl"][0];

                uac |= 0x2;
                entry.Properties["userAccountControl"].Value = uac;

                await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DisableUserAsync failed for {Dn}", userDn);
                return false;
            }
        }

        public async Task<bool> EnableUserAsync(string userDn)
        {
            if (string.IsNullOrWhiteSpace(userDn))
                return false;

            try
            {
                using var entry = GetEntry(userDn);
                entry.RefreshCache(new[] { "userAccountControl" });

                int uac = 0;
                if (entry.Properties["userAccountControl"].Count > 0)
                    uac = (int)entry.Properties["userAccountControl"][0];

                uac &= ~0x2;
                entry.Properties["userAccountControl"].Value = uac;

                await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnableUserAsync failed for {Dn}", userDn);
                return false;
            }
        }

        public async Task<bool> UnlockAccountAsync(string userDn)
        {
            try
            {
                using var entry = GetEntry(userDn);
                entry.Properties["lockoutTime"].Value = 0;

                await RunWithTimeout(() => entry.CommitChanges(), DefaultCommitTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UnlockAccountAsync failed for {userDn}", userDn);
                return false;
            }
        }

        public Task<string> ResetPasswordAsync(string userDn, string newPassword)
            => Task.FromResult(string.Empty);

        public Task SendSmsPassword(string userDn, string newPassword)
            => Task.CompletedTask;

        // Misc
        public async Task<string> GetPhoneNumber(string samAccountName)
        {
            if (string.IsNullOrWhiteSpace(samAccountName))
                return string.Empty;

            string escaped = LdapEscape(samAccountName);
            var res = await SearchAsync(
                $"(sAMAccountName={escaped})",
                _options.BaseDn,
                new[] { "mobile", "telephoneNumber" },
                sizeLimit: 1
            );

            var sr = res.FirstOrDefault();
            if (sr == null) return string.Empty;

            string mobile = GetProp(sr, "mobile");
            return !string.IsNullOrWhiteSpace(mobile) ? mobile : GetProp(sr, "telephoneNumber");
        }

        public Task<List<AdServer>> GetDistributionServersByProvince(string provinceDn)
            => Task.FromResult(new List<AdServer>());

        // OU Tasima
        public async Task<bool> MoveUserToOuAsync(string userDn, string targetOuDn)
        {
            if (string.IsNullOrWhiteSpace(userDn) || string.IsNullOrWhiteSpace(targetOuDn))
                return false;

            try
            {
                using var userEntry = GetEntry(userDn);
                using var targetOu = GetEntry(targetOuDn);

                await RunWithTimeout(() =>
                {
                    userEntry.MoveTo(targetOu);
                    userEntry.CommitChanges();
                }, DefaultCommitTimeout);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoveUserToOuAsync failed for {UserDn} ? {TargetOu}", userDn, targetOuDn);
                return false;
            }
        }

        // Bulk CSV
        public Task<List<string>> BulkCreateUsersFromCsvAsync(Stream csvStream)
            => Task.FromResult(new List<string> { "BulkCreateUsersFromCsvAsync henüz detayli implemente edilmedi." });

        public Task<List<string>> BulkDeleteUsersFromCsvAsync(Stream csvStream)
            => Task.FromResult(new List<string> { "BulkDeleteUsersFromCsvAsync henüz detayli implemente edilmedi." });

        // ===================================================
        // Timeout kalkani (CommitChanges vb. için)
        // ===================================================
        private static async Task RunWithTimeout(Action action, TimeSpan timeout)
        {
            var task = Task.Run(action);
            var done = await Task.WhenAny(task, Task.Delay(timeout));
            if (done != task)
                throw new TimeoutException($"AD operation exceeded timeout ({timeout.TotalSeconds}s).");
            await task; // exception propagate
        }

        public void Dispose()
        {
            // no-op
        }
    }
}
