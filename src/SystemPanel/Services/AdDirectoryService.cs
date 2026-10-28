// Dosya: Services/AdDirectoryService.cs
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemPanel.Configuration;

namespace SystemPanel.Services
{
    public class AdDirectoryService : IAdDirectoryService
    {
        private readonly ILogger<AdDirectoryService> _logger;
        private readonly LdapOptions _ldap;
        private readonly ActiveDirectoryOptions _ad;

        // Önceden sabitti: DC IP adresi, domain, BaseDn, local admin grup OU'su, kullanıcı OU'su,
        // hariç tutulan grup/OU adları ve "Şube Müdürü" unvanı. Artık yapılandırmadan okunur.
        private string BaseDn => _ldap.BaseDn;
        private string LocalAdminGroupsOu => _ad.Directory.LocalAdminGroupsOu;
        private string UsersBaseOu => _ad.Directory.UsersBaseOu;

        public AdDirectoryService(ILogger<AdDirectoryService> logger, IOptions<LdapOptions> ldap, IOptions<ActiveDirectoryOptions> ad)
        {
            _logger = logger;
            _ldap = ldap.Value;
            _ad = ad.Value;
        }

        private DirectoryEntry CreateEntry(string ldapPath)
        {
            if (string.IsNullOrWhiteSpace(ldapPath))
                throw new InvalidOperationException("ActiveDirectory:Directory OU yolu yapılandırılmamış.");
            return _ldap.CreateDirectoryEntry(ldapPath);
        }

        private AdUserDto MapUserFromDirectoryEntry(DirectoryEntry de, string? ouName = null)
        {
            string? dn = null;
            try
            {
                dn = de.Properties["distinguishedName"]?.Value?.ToString();
            }
            catch { }

            return new AdUserDto
            {
                DisplayName = de.Properties["displayname"]?.Value?.ToString(),
                Phone = de.Properties["mobile"]?.Value?.ToString(),
                DistinguishedName = dn,
                Title = de.Properties["title"]?.Value?.ToString(),
                OuName = ouName,
                Sicil = de.Properties["cn"]?.Value?.ToString()
            };
        }

        public Task<List<AdUserDto>> GetLocalAdminsAsync()
        {
            var result = new List<AdUserDto>();

            try
            {
                using var root = CreateEntry(LocalAdminGroupsOu);
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = "(objectClass=group)",
                    SearchScope = SearchScope.OneLevel
                };

                var groups = searcher.FindAll();
                using var ctx = new PrincipalContext(ContextType.Domain, _ad.Domain);

                foreach (SearchResult groupResult in groups)
                {
                    var groupEntry = groupResult.GetDirectoryEntry();
                    var groupName = groupEntry.Name.Replace("CN=", "", StringComparison.OrdinalIgnoreCase);

                    if (_ad.Scope.ExcludedLocalAdminGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var grp = GroupPrincipal.FindByIdentity(ctx, groupName);
                    if (grp == null) continue;

                    foreach (var principal in grp.GetMembers())
                    {
                        try
                        {
                            using var userEntry = CreateEntry(principal.DistinguishedName);
                            var dto = MapUserFromDirectoryEntry(userEntry, groupName);

                            if (!string.IsNullOrWhiteSpace(dto.Phone))
                                result.Add(dto);
                        }
                        catch (Exception exUser)
                        {
                            _logger.LogWarning(exUser, "Local admin kullanıcısı okunurken hata: {Dn}", principal.DistinguishedName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLocalAdminsAsync sırasında hata oluştu");
            }

            return Task.FromResult(result);
        }

        public Task<List<AdUserDto>> GetSubeMudurlariAsync()
        {
            var result = new List<AdUserDto>();

            try
            {
                using var root = CreateEntry(UsersBaseOu);
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = "(objectClass=organizationalUnit)",
                    SearchScope = SearchScope.OneLevel
                };

                var ous = searcher.FindAll();
                foreach (SearchResult ouResult in ous)
                {
                    var ouEntry = ouResult.GetDirectoryEntry();
                    var grupAdi = ouEntry.Name.Replace("OU=", "", StringComparison.OrdinalIgnoreCase);
                    if (_ad.Directory.ExcludedUserOus.Contains(grupAdi, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var dnValues = ouResult.Properties["distinguishedName"];
                    var ouPath = dnValues.Count > 0 ? dnValues[0]?.ToString() ?? "" : $"OU={grupAdi},{UsersBaseOu}";
                    using var entry2 = CreateEntry(ouPath);
                    using var search = new DirectorySearcher(entry2)
                    {
                        Filter = $"(&(objectClass=person)(title={LdapFilter.Escape(_ad.Directory.BranchManagerTitle)}))",
                        SearchScope = SearchScope.Subtree
                    };
                    search.PropertiesToLoad.Add("displayname");
                    search.PropertiesToLoad.Add("mobile");
                    search.PropertiesToLoad.Add("cn");
                    search.PropertiesToLoad.Add("title");
                    search.PropertiesToLoad.Add("distinguishedName");

                    foreach (SearchResult sr in search.FindAll())
                    {
                        try
                        {
                            var de = sr.GetDirectoryEntry();
                            var dto = MapUserFromDirectoryEntry(de, grupAdi);

                            if (!string.IsNullOrWhiteSpace(dto.Phone))
                                result.Add(dto);
                        }
                        catch (Exception exUser)
                        {
                            _logger.LogWarning(exUser, "Şube Müdürü okunurken hata");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSubeMudurlariAsync sırasında hata oluştu");
            }

            return Task.FromResult(result);
        }

        public Task<List<AdUserDto>> GetAllUsersAsync()
        {
            var result = new List<AdUserDto>();

            try
            {
                using var root = CreateEntry(UsersBaseOu);
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = "(objectClass=person)",
                    SearchScope = SearchScope.Subtree
                };

                searcher.PropertiesToLoad.Add("displayname");
                searcher.PropertiesToLoad.Add("mobile");
                searcher.PropertiesToLoad.Add("cn");
                searcher.PropertiesToLoad.Add("title");
                searcher.PropertiesToLoad.Add("distinguishedName");

                foreach (SearchResult sr in searcher.FindAll())
                {
                    try
                    {
                        var de = sr.GetDirectoryEntry();
                        var dto = MapUserFromDirectoryEntry(de);
                        if (!string.IsNullOrWhiteSpace(dto.Phone))
                            result.Add(dto);
                    }
                    catch (Exception exUser)
                    {
                        _logger.LogWarning(exUser, "GetAllUsersAsync içinde kullanıcı okunamadı");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllUsersAsync sırasında hata");
            }

            return Task.FromResult(result);
        }

        public Task<List<AdUserDto>> GetUsersByOuAsync(string ouDn)
        {
            var result = new List<AdUserDto>();

            if (string.IsNullOrWhiteSpace(ouDn))
                return Task.FromResult(result);

            try
            {
                using var entry = CreateEntry(ouDn);
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = "(objectClass=person)",
                    SearchScope = SearchScope.Subtree
                };
                searcher.PropertiesToLoad.Add("displayname");
                searcher.PropertiesToLoad.Add("mobile");
                searcher.PropertiesToLoad.Add("cn");
                searcher.PropertiesToLoad.Add("title");
                searcher.PropertiesToLoad.Add("distinguishedName");

                foreach (SearchResult sr in searcher.FindAll())
                {
                    try
                    {
                        var de = sr.GetDirectoryEntry();
                        var dto = MapUserFromDirectoryEntry(de);
                        if (!string.IsNullOrWhiteSpace(dto.Phone))
                            result.Add(dto);
                    }
                    catch (Exception exUser)
                    {
                        _logger.LogWarning(exUser, "GetUsersByOuAsync içinde kullanıcı okunamadı");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUsersByOuAsync sırasında hata. OU: {OuDn}", ouDn);
            }

            return Task.FromResult(result);
        }

        public Task<AdUserDto?> GetUserBySicilAsync(string sicil)
        {
            if (string.IsNullOrWhiteSpace(sicil))
                return Task.FromResult<AdUserDto?>(null);

            try
            {
                using var root = CreateEntry(UsersBaseOu);
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = $"(&(objectClass=person)(cn={LdapFilter.Escape(sicil)}))", // önceden escape yoktu (LDAP injection)
                    SearchScope = SearchScope.Subtree
                };

                searcher.PropertiesToLoad.Add("displayname");
                searcher.PropertiesToLoad.Add("mobile");
                searcher.PropertiesToLoad.Add("cn");
                searcher.PropertiesToLoad.Add("title");
                searcher.PropertiesToLoad.Add("distinguishedName");

                var sr = searcher.FindOne();
                if (sr == null) return Task.FromResult<AdUserDto?>(null);

                var de = sr.GetDirectoryEntry();
                var dto = MapUserFromDirectoryEntry(de);
                return Task.FromResult<AdUserDto?>(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUserBySicilAsync sırasında hata. Sicil: {Sicil}", sicil);
                return Task.FromResult<AdUserDto?>(null);
            }
        }

        public Task<List<AdUserDto>> GetUsersByDnsAsync(IEnumerable<string> dns)
        {
            var result = new List<AdUserDto>();

            foreach (var dn in dns.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                try
                {
                    using var userEntry = CreateEntry(dn);
                    var dto = MapUserFromDirectoryEntry(userEntry);
                    if (!string.IsNullOrWhiteSpace(dto.Phone))
                        result.Add(dto);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetUsersByDnsAsync sırasında kullanıcı okunamadı: {Dn}", dn);
                }
            }

            return Task.FromResult(result);
        }
    }
}
