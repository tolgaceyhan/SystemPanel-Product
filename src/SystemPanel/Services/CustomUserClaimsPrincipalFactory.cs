using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Security.Claims;
using SystemPanel.Configuration;
using SystemPanel.Security.Scope;

namespace SystemPanel.Services
{
    /// <summary>
    /// Oturum açılırken AD'den kullanıcı bilgilerini, rol eşlemesini (RoleMapping) ve
    /// organizasyonel kapsamı (ou:root) claim olarak ekler. AD'ye ulaşılamazsa claim'ler
    /// eklenmez; bu durumda kullanıcı yalnızca Identity rollerine sahip olur ve kapsamı
    /// boştur (fail-closed).
    /// </summary>
    public class CustomUserClaimsPrincipalFactory
        : UserClaimsPrincipalFactory<IdentityUser, IdentityRole>
    {
        private readonly ILogger<CustomUserClaimsPrincipalFactory> _logger;
        private readonly RoleMappingOptions _roleMap;
        private readonly LdapOptions _ldap;
        private readonly ActiveDirectoryOptions _ad;

        public CustomUserClaimsPrincipalFactory(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<CustomUserClaimsPrincipalFactory> logger,
            IOptions<IdentityOptions> optionsAccessor,
            IOptions<RoleMappingOptions> roleMapping,
            IOptions<LdapOptions> ldap,
            IOptions<ActiveDirectoryOptions> ad)
            : base(userManager, roleManager, optionsAccessor)
        {
            _logger = logger;
            _roleMap = roleMapping.Value;
            _ldap = ldap.Value;
            _ad = ad.Value;
        }

        public override async System.Threading.Tasks.Task<ClaimsPrincipal> CreateAsync(IdentityUser user)
        {
            var principal = await base.CreateAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;

            if (string.IsNullOrWhiteSpace(_ldap.BaseDn))
                return principal; // AD yapılandırılmamış (ör. yalnızca yerel hesaplar)

            try
            {
                using var entry = _ldap.CreateDirectoryEntry(_ldap.BaseDn);
                using var ds = new DirectorySearcher(entry)
                {
                    Filter = "(&(objectCategory=person)(objectClass=user)(|" +
                             $"(sAMAccountName={LdapFilter.Escape(user.UserName)})" +
                             $"(userPrincipalName={LdapFilter.Escape(user.UserName)})" +
                             (string.IsNullOrEmpty(user.Email) ? "" : $"(userPrincipalName={LdapFilter.Escape(user.Email)})") +
                             "))",
                    SizeLimit = 2,
                    ClientTimeout = TimeSpan.FromSeconds(_ldap.ClientTimeoutSeconds),
                    ServerTimeLimit = TimeSpan.FromSeconds(_ldap.ServerTimeLimitSeconds),
                };

                ds.PropertiesToLoad.AddRange(new[] { "sAMAccountName", "distinguishedName", "memberOf", "userPrincipalName", "mail" });

                var results = ds.FindAll();
                if (results.Count != 1)
                {
                    // 0: AD kullanıcısı değil (yerel hesap). >1: belirsiz eşleşme → kapsam verilmez.
                    if (results.Count > 1)
                        _logger.LogWarning("AD'de birden fazla eşleşme bulundu; kapsam claim'i eklenmedi. User={User}", user.UserName);
                    return principal;
                }

                var sr = results[0];
                var adDn = GetProp(sr, "distinguishedName");

                AddIfPresent(identity, ScopeClaimTypes.AdSam, GetProp(sr, "sAMAccountName"));
                AddIfPresent(identity, ScopeClaimTypes.AdDn, adDn);
                AddIfPresent(identity, "ad:upn", GetProp(sr, "userPrincipalName"));
                AddIfPresent(identity, "ad:mail", GetProp(sr, "mail"));

                var groupDns = GetMemberOf(sr);
                AddRolesFromAdGroups(identity, groupDns);

                // Organizasyonel kapsam: kullanıcının kendi DN'inden türetilen il OU kökleri.
                var roots = ScopeDerivation.DeriveRoots(adDn, _ad.Scope);
                foreach (var root in roots)
                    identity.AddClaim(new Claim(ScopeClaimTypes.OuRoot, root.ToString()));

                var province = ScopeDerivation.ProvinceName(roots);
                if (!string.IsNullOrWhiteSpace(province))
                    identity.AddClaim(new Claim("Province", province));

                // SCCM boundary eşlemesi için il yöneticisi gruplarından il adları.
                foreach (var p in GetLocalAdminProvinces(groupDns))
                    identity.AddClaim(new Claim(ScopeClaimTypes.Province, p));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AD claim'leri oluşturulamadı. User={User}", user.UserName);
            }

            return principal;
        }

        private void AddRolesFromAdGroups(ClaimsIdentity identity, List<string> groupDns)
        {
            var groupNames = groupDns.Select(ExtractCn).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (role, adGroups) in _roleMap)
            {
                if (adGroups != null && adGroups.Any(groupNames.Contains) && !identity.HasClaim(ClaimTypes.Role, role))
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            // Önceki sürüm eşleşme olmadığında herkese "Monitoring" rolü ekliyordu (istenmeyen yetki).
        }

        private IEnumerable<string> GetLocalAdminProvinces(List<string> groupDns)
        {
            var suffix = _ad.Scope.LocalAdminGroupSuffix;
            if (string.IsNullOrWhiteSpace(suffix))
                yield break;

            foreach (var dn in groupDns)
            {
                var cn = ExtractCn(dn);
                if (!cn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_ad.Scope.ExcludedLocalAdminGroups.Contains(cn, StringComparer.OrdinalIgnoreCase))
                    continue;

                var province = ReadGroupProvince(dn);
                if (!string.IsNullOrWhiteSpace(province))
                    yield return province;
            }
        }

        // Grup açıklamasının ilk kelimesi il adıdır (ör. "Adana Local Admin"). Mevcut kurumsal düzene uyum için korunmuştur.
        private string? ReadGroupProvince(string groupDn)
        {
            try
            {
                using var groupEntry = _ldap.CreateDirectoryEntry(groupDn);
                groupEntry.RefreshCache(new[] { "description" });
                var desc = groupEntry.Properties["description"]?.Value?.ToString() ?? "";
                return desc.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Grup açıklaması okunamadı.");
                return null;
            }
        }

        private static void AddIfPresent(ClaimsIdentity identity, string type, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                identity.AddClaim(new Claim(type, value));
        }

        private static List<string> GetMemberOf(SearchResult sr)
        {
            var list = new List<string>();
            if (sr.Properties.Contains("memberOf"))
            {
                foreach (var item in sr.Properties["memberOf"])
                    list.Add(item?.ToString() ?? "");
            }
            return list;
        }

        private static string ExtractCn(string dn)
            => DistinguishedName.TryParse(dn, out var parsed) && parsed.Rdns[0].Type == "CN" ? parsed.Rdns[0].Value : string.Empty;

        private static string? GetProp(SearchResult r, string p)
            => r.Properties.Contains(p) && r.Properties[p].Count > 0 ? r.Properties[p][0]?.ToString() : null;
    }

    /// <summary>RFC 4515 LDAP filtre değeri kaçışı (tek ortak implementasyon).</summary>
    public static class LdapFilter
    {
        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\5c"); break;
                    case '*': sb.Append("\\2a"); break;
                    case '(': sb.Append("\\28"); break;
                    case ')': sb.Append("\\29"); break;
                    case '\0': sb.Append("\\00"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
