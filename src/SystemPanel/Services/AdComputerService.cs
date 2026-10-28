using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SystemPanel.Configuration;
using SystemPanel.Models;
using SystemPanel.Services.ActiveDirectory;

namespace SystemPanel.Services
{
    public class AdComputerService : IAdComputerService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AdComputerService> _logger;

        private readonly LdapOptions _ldap;
        private readonly string _baseDn;

        // Discover ayarları
        private readonly int _pageSize;
        private readonly int _dnsMaxConcurrency;
        private readonly int _dnsTimeoutMs;

        public AdComputerService(IConfiguration config, ILogger<AdComputerService> logger, Microsoft.Extensions.Options.IOptions<LdapOptions> ldap)
        {
            _config = config;
            _logger = logger;
            _ldap = ldap.Value;
            _baseDn = (_ldap.BaseDn ?? "").Trim();

            _pageSize = int.TryParse(_config["Ldap:PageSize"], out var ps) ? Math.Clamp(ps, 100, 2000) : 500;
            _dnsMaxConcurrency = int.TryParse(_config["Ldap:DnsMaxConcurrency"], out var mc) ? Math.Clamp(mc, 1, 64) : 8;
            _dnsTimeoutMs = int.TryParse(_config["Ldap:DnsTimeoutMs"], out var dt) ? Math.Clamp(dt, 200, 10000) : 1500;
        }

        // =========================================================
        //  CONNECTION HELPERS
        // =========================================================

        private DirectoryEntry ConnectByDn(string? dnOrBase = null)
        {
            var dn = string.IsNullOrWhiteSpace(dnOrBase) ? _baseDn : dnOrBase.Trim();
            return _ldap.CreateDirectoryEntry(dn);
        }

        private static void ConfigureSearcher(DirectorySearcher searcher, string filter, string[] props, int pageSize, ReferralChasingOption referrals)
        {
            searcher.Filter = filter;
            searcher.SearchScope = SearchScope.Subtree;
            searcher.PageSize = pageSize;
            searcher.ReferralChasing = referrals;

            searcher.PropertiesToLoad.Clear();
            foreach (var p in props)
                searcher.PropertiesToLoad.Add(p);
        }

        private async Task<List<SearchResult>> SearchAsync(
            string filter,
            string? searchDn,
            string[] props,
            int sizeLimit = 0,
            ReferralChasingOption referrals = ReferralChasingOption.All,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var list = new List<SearchResult>();

                try
                {
                    using var entry = ConnectByDn(searchDn);
                    using var searcher = new DirectorySearcher(entry);

                    ConfigureSearcher(searcher, filter, props, _pageSize, referrals);

                    if (sizeLimit > 0)
                        searcher.SizeLimit = sizeLimit;

                    using var results = searcher.FindAll();
                    foreach (SearchResult r in results)
                    {
                        ct.ThrowIfCancellationRequested();
                        list.Add(r);
                    }
                }
                catch (DirectoryServicesCOMException ex)
                {
                    _logger.LogError(ex, "LDAP search failed. Filter={Filter}, SearchDn={SearchDn}", filter, searchDn);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LDAP search unexpected error. Filter={Filter}, SearchDn={SearchDn}", filter, searchDn);
                }

                return list;
            }, ct);
        }

        private static string? GetProp(SearchResult r, string prop)
        {
            if (r?.Properties == null) return null;
            if (!r.Properties.Contains(prop)) return null;
            if (r.Properties[prop] == null || r.Properties[prop].Count == 0) return null;
            return r.Properties[prop][0]?.ToString();
        }

        // =========================================================
        //  IAdComputerService - COMPUTERS
        // =========================================================

        public async Task<List<AdComputer>> GetAllComputersAsync(string? searchTerm = null, CancellationToken ct = default)
        {
            var safe = EscapeLdapFilter(searchTerm);

            var filter = string.IsNullOrWhiteSpace(safe)
                ? "(&(objectCategory=computer))"
                : $"(&(objectCategory=computer)(|(cn=*{safe}*)(name=*{safe}*)(dNSHostName=*{safe}*)))";

            var props = new[]
            {
                "cn","name","dNSHostName","operatingSystem","description",
                "distinguishedName","lastLogonTimestamp","userAccountControl"
            };

            var results = await SearchAsync(filter, _baseDn, props, 0, ReferralChasingOption.All, ct);
            return results.Select(MapComputer).Where(x => x != null).ToList()!;
        }

        // ✅ Interface eski sürüm “GetComputerByNameAsync(string)” istiyorsa:
        public Task<AdComputer?> GetComputerByNameAsync(string name) =>
            GetComputerByNameAsync(name, CancellationToken.None);

        public async Task<AdComputer?> GetComputerByNameAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var safe = EscapeLdapFilter(name);

            var filter =
                $"(&(objectCategory=computer)(|(cn={safe})(name={safe})(sAMAccountName={safe}$)(dNSHostName={safe})))";

            var props = new[]
            {
                "cn","name","dNSHostName","operatingSystem","description",
                "distinguishedName","lastLogonTimestamp","userAccountControl"
            };

            var results = await SearchAsync(filter, _baseDn, props, 1, ReferralChasingOption.All, ct);
            return results.Count > 0 ? MapComputer(results[0]) : null;
        }

        public async Task<List<AdComputer>> GetComputersByOuAsync(string ou, string? searchTerm = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ou))
                return new List<AdComputer>();

            var ouDn = NormalizeOuToDn(ou);

            var safe = EscapeLdapFilter(searchTerm);
            var filter = string.IsNullOrWhiteSpace(safe)
                ? "(&(objectCategory=computer))"
                : $"(&(objectCategory=computer)(|(cn=*{safe}*)(name=*{safe}*)(dNSHostName=*{safe}*)))";

            var props = new[]
            {
                "cn","name","dNSHostName","operatingSystem","description",
                "distinguishedName","lastLogonTimestamp","userAccountControl"
            };

            var results = await SearchAsync(filter, ouDn, props, 0, ReferralChasingOption.All, ct);
            return results.Select(MapComputer).Where(x => x != null).ToList()!;
        }

        public async Task<List<string>> GetAllServerNamesAsync(CancellationToken ct = default)
        {
            var filter = "(&(objectCategory=computer)(operatingSystem=*Server*))";
            var props = new[] { "name" };

            var results = await SearchAsync(filter, _baseDn, props, 0, ReferralChasingOption.All, ct);

            return results
                .Select(r => GetProp(r, "name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList()!;
        }

        public async Task<string> GetComputerDistinguishedNameAsync(string computerName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(computerName))
                return string.Empty;

            var safe = EscapeLdapFilter(computerName);
            var filter =
                $"(&(objectCategory=computer)(|(cn={safe})(name={safe})(sAMAccountName={safe}$)(dNSHostName={safe})))";

            var results = await SearchAsync(filter, _baseDn, new[] { "distinguishedName" }, 1, ReferralChasingOption.All, ct);
            return results.Count > 0 ? (GetProp(results[0], "distinguishedName") ?? "") : "";
        }

        /// <summary>
        /// Windows LAPS ve legacy LAPS özniteliklerini okur. Değer saklanmaz/loglanmaz; çağıran
        /// (AdComputerOperations) izin, kapsam ve audit kontrolünü yapar.
        /// </summary>
        public async Task<LapsResult> ReadLapsAsync(string computerDn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(computerDn))
                return LapsResult.NotFound("Bilgisayar bulunamadı.");

            return await Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using var entry = ConnectByDn(computerDn);
                    entry.RefreshCache(new[]
                    {
                        "msLAPS-Password", "msLAPS-PasswordExpirationTime", "msLAPS-EncryptedPassword",
                        "ms-Mcs-AdmPwd", "ms-Mcs-AdmPwdExpirationTime",
                    });

                    return LapsResult.FromAttributes(
                        entry.Properties["msLAPS-Password"]?.Value?.ToString(),
                        ReadLargeInteger(entry, "msLAPS-PasswordExpirationTime"),
                        entry.Properties.Contains("msLAPS-EncryptedPassword") && entry.Properties["msLAPS-EncryptedPassword"].Count > 0,
                        entry.Properties["ms-Mcs-AdmPwd"]?.Value?.ToString(),
                        ReadLargeInteger(entry, "ms-Mcs-AdmPwdExpirationTime"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Değerin kendisi asla loglanmaz.
                    _logger.LogWarning(ex, "LAPS okunamadı: {Dn}", computerDn);
                    return LapsResult.NotFound("LAPS okunamadı (şema veya yetki).");
                }
            }, ct);
        }

        private static long? ReadLargeInteger(DirectoryEntry entry, string attribute)
        {
            if (!entry.Properties.Contains(attribute) || entry.Properties[attribute].Count == 0)
                return null;

            var value = entry.Properties[attribute][0];
            if (value is long l) return l;
            if (long.TryParse(value?.ToString(), out var parsed)) return parsed;

            // ADSI LargeInteger COM nesnesi (HighPart/LowPart)
            try
            {
                var type = value!.GetType();
                var high = (int)type.InvokeMember("HighPart", System.Reflection.BindingFlags.GetProperty, null, value, null)!;
                var low = (int)type.InvokeMember("LowPart", System.Reflection.BindingFlags.GetProperty, null, value, null)!;
                return ((long)high << 32) | (uint)low;
            }
            catch
            {
                return null;
            }
        }

        // =========================================================
        //  DISCOVER SERVERS (AD)
        // =========================================================

        public async Task<List<AdServer>> DiscoverServersAsync(string? ou, string? ipPrefix)
        {
            // Referral hatası burada çok görülür -> None daha stabil
            var referrals = ReferralChasingOption.None;

            var searchDn = string.IsNullOrWhiteSpace(ou)
                ? _baseDn
                : NormalizeOuToDn(ou);

            var filter = "(&(objectCategory=computer)(operatingSystem=*Server*))";

            var props = new[]
            {
                "name",
                "dNSHostName",
                "operatingSystem",
                "description",
                "distinguishedName",
                "lastLogonTimestamp"
            };

            var results = await SearchAsync(filter, searchDn, props, 0, referrals, CancellationToken.None);

            var servers = results.Select(r =>
            {
                var dn = GetProp(r, "distinguishedName");
                return new AdServer
                {
                    Name = GetProp(r, "name") ?? "",
                    OperatingSystem = GetProp(r, "operatingSystem"),
                    Description = GetProp(r, "description"),
                    DistinguishedName = dn,
                    OrganizationalUnit = dn,
                    LastLogonDate = ParseLastLogon(r.Properties["lastLogonTimestamp"])
                    // IpAddress sonra doldurulacak
                };
            }).ToList();

            // IP resolve (timeout + concurrency)
            await ResolveIpsAsync(servers);

            if (!string.IsNullOrWhiteSpace(ipPrefix))
            {
                servers = servers
                    .Where(s => !string.IsNullOrWhiteSpace(s.IpAddress) && s.IpAddress.StartsWith(ipPrefix))
                    .ToList();
            }

            return servers;
        }

        // =========================================================
        //  MAPPING / HELPERS
        // =========================================================

        private AdComputer? MapComputer(SearchResult r)
        {
            try
            {
                var dn = GetProp(r, "distinguishedName");

                return new AdComputer
                {
                    Cn = GetProp(r, "cn"),
                    Name = GetProp(r, "name"),
                    DistinguishedName = dn,
                    OperatingSystem = GetProp(r, "operatingSystem"),
                    Description = GetProp(r, "description"),
                    LastLogon = ParseLastLogon(r.Properties["lastLogonTimestamp"]),
                    IsEnabled = !IsAccountDisabled(GetProp(r, "userAccountControl")),
                    // LAPS parolası liste/detay sorgularında ÇEKİLMEZ; yalnızca ReadLapsAsync ile, izin + audit sonrası.
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MapComputer failed");
                return null;
            }
        }

        private static DateTime? ParseLastLogon(ResultPropertyValueCollection values)
        {
            if (values == null || values.Count == 0) return null;
            if (!long.TryParse(values[0]?.ToString(), out long fileTime)) return null;
            if (fileTime <= 0) return null;
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }

        private static bool IsAccountDisabled(string? uac)
        {
            if (int.TryParse(uac, out int v))
                return (v & 0x2) != 0;
            return false;
        }

        private string NormalizeOuToDn(string ou)
        {
            var s = ou.Trim();

            // DN gibi geldiyse aynen kullan
            if (s.Contains("DC=", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("OU=", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }

            // sadece OU adı geldiyse
            var ouRdn = EscapeRdn(s);
            return $"OU={ouRdn},{_baseDn}";
        }

        private static string EscapeLdapFilter(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\5c")
                    .Replace("*", "\\2a")
                    .Replace("(", "\\28")
                    .Replace(")", "\\29")
                    .Replace("\0", "\\00");
        }

        private static string EscapeRdn(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace(",", "\\,");
        }

        private async Task ResolveIpsAsync(List<AdServer> servers)
        {
            if (servers == null || servers.Count == 0) return;

            using var sem = new SemaphoreSlim(_dnsMaxConcurrency);

            var tasks = servers.Select(async s =>
            {
                await sem.WaitAsync();
                try
                {
                    var host = s.Name;
                    var ip = await ResolveIpWithTimeoutAsync(host, _dnsTimeoutMs);
                    s.IpAddress = ip ?? "";
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private static async Task<string?> ResolveIpWithTimeoutAsync(string? host, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(host)) return null;

            try
            {
                var dnsTask = Dns.GetHostAddressesAsync(host.Trim());
                var done = await Task.WhenAny(dnsTask, Task.Delay(timeoutMs));

                if (done != dnsTask) return null;

                var addrs = await dnsTask;
                return addrs
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.ToString();
            }
            catch
            {
                return null;
            }
        }
        public async Task<AdServer?> GetServerByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var safe = EscapeLdapFilter(name);

            var filter =
                $"(&(objectCategory=computer)(operatingSystem=*Server*)(|(cn={safe})(name={safe})(sAMAccountName={safe}$)(dNSHostName={safe})))";

            var props = new[]
            {
                "name",
                "dNSHostName",
                "operatingSystem",
                "description",
                "distinguishedName",
                "lastLogonTimestamp"
            };

            var results = await SearchAsync(filter, _baseDn, props, 1, ReferralChasingOption.None, CancellationToken.None);
            if (results.Count == 0)
                return null;

            var result = results[0];
            var dn = GetProp(result, "distinguishedName");

            return new AdServer
            {
                Name = GetProp(result, "name") ?? "",
                OperatingSystem = GetProp(result, "operatingSystem"),
                Description = GetProp(result, "description"),
                DistinguishedName = dn,
                OrganizationalUnit = dn,
                LastLogonDate = ParseLastLogon(result.Properties["lastLogonTimestamp"])
            };
        }
    }
}
